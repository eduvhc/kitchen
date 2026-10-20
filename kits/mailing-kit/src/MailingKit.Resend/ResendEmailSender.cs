using System.Buffers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MailingKit.Transport;
using Microsoft.Extensions.Logging;
using ResultingKit;

namespace MailingKit.Resend;

/// <summary>
/// Sends email through the Resend HTTP API (<c>POST /emails</c>). Register it with
/// <see cref="ResendServiceCollectionExtensions.AddResendTransport(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration)"/>,
/// which supplies an <see cref="HttpClient"/> with the base address, API key and timeout already set.
/// </summary>
/// <remarks>
/// Timeouts, network failures, <c>429</c> and <c>5xx</c> come back as <see cref="ErrorKind.Transient"/>,
/// so the inbox retries them. Every other rejection is permanent. Error codes are <c>resend.</c> followed
/// by Resend's own error name, such as <c>resend.validation_error</c>.
/// <para>
/// <see cref="OutgoingEmail.IdempotencyKey"/> is sent as Resend's <c>Idempotency-Key</c> header, so a
/// retry after a lost response returns the original email instead of sending a second one. Resend
/// honours a key for 24 hours; a retry with the same key but a different payload (a template edited
/// between attempts, say) is rejected with <c>409 invalid_idempotent_request</c>, which is permanent.
/// </para>
/// </remarks>
/// <param name="http">A client whose base address is the Resend API root and which carries the API key.</param>
/// <param name="logger">Logs each send and each failure.</param>
public sealed class ResendEmailSender(HttpClient http, ILogger<ResendEmailSender> logger) : IEmailSender
{
    private const int MaxRawBodyInMessage = 500;

    internal const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly SearchValues<char> Specials = SearchValues.Create("\"(),.:;<>@[\\]");

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        ct.ThrowIfCancellationRequested();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
            {
                Content = JsonContent.Create(Map(email), ResendJsonContext.Default.ResendEmailRequest),
            };

            if (!string.IsNullOrWhiteSpace(email.IdempotencyKey))
            {
                request.Headers.Add(IdempotencyKeyHeader, email.IdempotencyKey);
            }

            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var id = TryDeserialize(body, ResendJsonContext.Default.ResendEmailResponse)?.Id;

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Resend accepted email {ProviderMessageId} to {RecipientCount} recipients", id, email.To.Count);
                }

                // Resend accepted it; failing here would make the inbox send it again.
                return new SendReceipt(id);
            }

            var error = ToError(response.StatusCode, body);
            logger.LogWarning("Resend rejected email {Subject}: {Error}", email.Subject, error);
            return error;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Not the caller's token, so HttpClient.Timeout elapsed.
            logger.LogWarning(ex, "Resend timed out for {Subject}", email.Subject);
            return Error.Transient("resend.timeout", $"No response from Resend within {http.Timeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not reach Resend for {Subject}", email.Subject);
            return Error.Transient("resend.unavailable", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static ResendEmailRequest Map(OutgoingEmail email) => new()
    {
        From = FormatMailbox(email.FromAddress, email.FromName),
        To = email.To,
        Cc = email.Cc.Count > 0 ? email.Cc : null,
        Bcc = email.Bcc.Count > 0 ? email.Bcc : null,
        ReplyTo = string.IsNullOrWhiteSpace(email.ReplyTo) ? null : email.ReplyTo,
        Subject = email.Subject,
        Html = email.HtmlBody,
        Text = email.TextBody,
        Headers = email.Headers.Count > 0 ? email.Headers : null,
        Attachments = email.Attachments.Count > 0
            ? email.Attachments
                .Select(a => new ResendAttachment
                {
                    Content = a.Content,
                    FileName = a.FileName,
                    ContentType = string.IsNullOrWhiteSpace(a.ContentType) ? null : a.ContentType,
                    ContentId = string.IsNullOrWhiteSpace(a.ContentId) ? null : a.ContentId,
                })
                .ToList()
            : null,
    };

    /// <summary>Formats <c>Name &lt;address&gt;</c>, quoting the name when RFC 5322 requires it.</summary>
    internal static string FormatMailbox(string address, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return address;
        }

        var needsQuoting = name.AsSpan().ContainsAny(Specials);
        var display = needsQuoting
            ? $"\"{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : name;

        return $"{display} <{address}>";
    }

    internal static Error ToError(HttpStatusCode status, string body)
    {
        var code = (int)status;
        var parsed = TryDeserialize(body, ResendJsonContext.Default.ResendErrorResponse);

        var errorCode = string.IsNullOrWhiteSpace(parsed?.Name) ? $"resend.http_{code}" : $"resend.{parsed.Name}";
        var message = $"{code}: {Describe(parsed?.Message, body, status)}";

        return code switch
        {
            429 or 408 or >= 500 => Error.Transient(errorCode, message),
            // Another request with the same idempotency key is still in flight; retrying resolves it.
            409 when parsed?.Name == "concurrent_idempotent_requests" => Error.Transient(errorCode, message),
            409 => Error.Conflict(errorCode, message),
            422 => Error.Validation(errorCode, message),
            401 => Error.Unauthorized(errorCode, message),
            403 => Error.Forbidden(errorCode, message),
            _ => Error.Failure(errorCode, message),
        };
    }

    private static string Describe(string? message, string body, HttpStatusCode status)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            var trimmed = body.Trim();
            return trimmed.Length > MaxRawBodyInMessage ? trimmed[..MaxRawBodyInMessage] + "…" : trimmed;
        }

        return status.ToString();
    }

    private static T? TryDeserialize<T>(string body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(body, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
