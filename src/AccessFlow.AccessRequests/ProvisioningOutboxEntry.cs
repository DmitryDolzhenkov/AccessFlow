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
    // Calls made to the System so far.
    public int AttemptCount { get; private set; }
    // The entry is not processed before this time (BR-20).
    public DateTimeOffset NextAttemptAt { get; private set; }

    public static ProvisioningOutboxEntry For(Guid accessRequestId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            AccessRequestId = accessRequestId,
            CreatedAt = now,
            NextAttemptAt = now,
        };

    public void MarkProcessed(DateTimeOffset now) => ProcessedAt = now;

    // The attempt completed Provisioning, successfully or not.
    public void MarkProcessedAfterAttempt(DateTimeOffset now)
    {
        AttemptCount++;
        ProcessedAt = now;
    }

    // BR-20: after the n-th attempt the next one waits baseDelay × 2^(n−1).
    public void ScheduleRetry(DateTimeOffset now, TimeSpan baseDelay)
    {
        AttemptCount++;
        NextAttemptAt = now + baseDelay * Math.Pow(2, AttemptCount - 1);
    }
}
