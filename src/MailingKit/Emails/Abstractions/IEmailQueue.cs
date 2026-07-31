using MailingKit.Emails.Domain;

namespace MailingKit.Emails.Abstractions;

/// <summary>
/// Read and lifecycle operations products need. The dispatcher's claim/mark operations live on
/// <see cref="IEmailDispatchStore"/> so they do not surface here.
/// </summary>
public interface IEmailQueue
{
    /// <summary>
    /// Stages an email on the host's change tracker. Does not save — the caller's
    /// <c>SaveChangesAsync</c> commits it alongside their own work.
    /// </summary>
    void Enqueue(EmailMessage message);

    Task<EmailMessage?> FindAsync(Guid id, CancellationToken ct = default);

    Task<EmailMessage?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);

    /// <summary>Cancels a still-queued email. Writes immediately; not part of the caller's transaction.</summary>
    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);
}
