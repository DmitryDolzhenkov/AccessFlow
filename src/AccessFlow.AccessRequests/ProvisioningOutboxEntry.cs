namespace AccessFlow.AccessRequests;

/// <summary>
/// A record that an Approved Access Request needs Provisioning (BR-16, ADR 0002).
/// Saved with the approval and processed later by the background Provisioning worker.
/// </summary>
internal sealed class ProvisioningOutboxEntry
{
    private ProvisioningOutboxEntry()
    {
    }

    public Guid Id { get; private init; }
    public Guid AccessRequestId { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }
    // Null until the entry no longer needs processing.
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static ProvisioningOutboxEntry For(Guid accessRequestId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            AccessRequestId = accessRequestId,
            CreatedAt = now,
        };

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;
}
