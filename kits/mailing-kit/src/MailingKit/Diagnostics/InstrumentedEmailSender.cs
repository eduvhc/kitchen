using System.Diagnostics;
using MailingKit.Transport;
using ResultingKit;

namespace MailingKit.Diagnostics;

/// <summary>
/// Wraps the host's transport so every send emits a span and metrics, whichever transport it is.
/// </summary>
internal sealed class InstrumentedEmailSender : IEmailSender
{
    private readonly IEmailSender _inner;
    private readonly MailingKitMetrics _metrics;

    public InstrumentedEmailSender(IEmailSender inner, MailingKitMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(metrics);

        _inner = inner;
        _metrics = metrics;
        Provider = ProviderName(inner.GetType());
    }

    /// <summary>The low-cardinality transport name reported as <c>mailingkit.provider</c>.</summary>
    public string Provider { get; }

    public static IEmailSender Wrap(IEmailSender sender, MailingKitMetrics metrics) =>
        sender as InstrumentedEmailSender ?? new InstrumentedEmailSender(sender, metrics);

    public async Task<Result<SendReceipt>> SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        var start = Stopwatch.GetTimestamp();
        using var activity = MailingKitInstrumentation.ActivitySource.StartActivity(MailingKitTags.SendActivityName, ActivityKind.Client);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(MailingKitTags.Provider, Provider);
            activity.SetTag(MailingKitTags.RecipientCount, email.To.Count + email.Cc.Count + email.Bcc.Count);
            activity.SetTag(MailingKitTags.AttachmentCount, email.Attachments.Count);
        }

        Result<SendReceipt> result;

        try
        {
            result = await _inner.SendAsync(email, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errorType = ex.GetType().FullName ?? ex.GetType().Name;

            if (activity is not null)
            {
                activity.SetTag(MailingKitTags.Outcome, MailingKitTags.Failed);
                activity.SetTag(MailingKitTags.ErrorType, errorType);
                activity.SetStatus(ActivityStatusCode.Error);
                activity.AddException(ex);
            }

            _metrics.RecordSend(Provider, MailingKitTags.Failed, errorType, errorKind: null, Stopwatch.GetElapsedTime(start));
            throw;
        }

        if (result.IsSuccess)
        {
            if (activity is not null)
            {
                activity.SetTag(MailingKitTags.Outcome, MailingKitTags.Sent);
                activity.SetTag(MailingKitTags.ProviderMessageId, result.Value.ProviderMessageId);
            }

            _metrics.RecordSend(Provider, MailingKitTags.Sent, errorType: null, errorKind: null, Stopwatch.GetElapsedTime(start));
            return result;
        }

        var error = result.Error;
        var kind = MailingKitTags.ToTagValue(error.Kind);

        if (activity is not null)
        {
            activity.SetTag(MailingKitTags.Outcome, MailingKitTags.Failed);
            activity.SetTag(MailingKitTags.ErrorType, error.Code);
            activity.SetTag(MailingKitTags.ErrorKind, kind);

            // The code, not the message: provider messages can echo recipient addresses.
            activity.SetStatus(ActivityStatusCode.Error, error.Code);
        }

        _metrics.RecordSend(Provider, MailingKitTags.Failed, error.Code, kind, Stopwatch.GetElapsedTime(start));
        return result;
    }

    /// <summary>
    /// <c>SmtpEmailSender</c> → <c>smtp</c>, <c>ResendEmailSender</c> → <c>resend</c>, <c>AcmeMailSender</c> → <c>acme</c>.
    /// </summary>
    internal static string ProviderName(Type senderType)
    {
        var name = senderType.Name;
        var arity = name.IndexOf('`', StringComparison.Ordinal);

        if (arity > 0)
        {
            name = name[..arity];
        }

        foreach (var suffix in (ReadOnlySpan<string>)["EmailSender", "MailSender", "Sender"])
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        }

        return string.Create(name.Length, name, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = char.ToLowerInvariant(source[i]);
            }
        });
    }
}
