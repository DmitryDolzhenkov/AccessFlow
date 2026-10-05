namespace AccessFlow.AccessRequests;

public enum AuditEvent
{
    Created,
    Approved,
    Rejected,
    Cancelled,
    Provisioned,
    ProvisioningFailed,
}

/// <summary>
/// An immutable Audit Log record of a change to an Access Request (BR-23, BR-24, BR-26).
/// </summary>
internal sealed class AuditLogEntry
{
    private AuditLogEntry()
    {
    }

    public Guid Id { get; private init; }
    // Insertion order assigned by the database; breaks ties between entries with the same OccurredAt.
    public long Sequence { get; private init; }
    public Guid AccessRequestId { get; private init; }
    public AuditEvent Event { get; private init; }
    // Null when the actor is AccessFlow itself rather than a User (BR-25).
    public Guid? ActorId { get; private init; }
    public DateTimeOffset OccurredAt { get; private init; }
    public AccessRequestStatus? StatusBefore { get; private init; }
    public AccessRequestStatus StatusAfter { get; private init; }
    public string? Justification { get; private init; }
    public string? Comment { get; private init; }
    public string? RejectionReason { get; private init; }

    public static AuditLogEntry Created(AccessRequest request) =>
        new()
        {
            Id = Guid.CreateVersion7(request.CreatedAt),
            AccessRequestId = request.Id,
            Event = AuditEvent.Created,
            ActorId = request.RequesterId,
            OccurredAt = request.CreatedAt,
            StatusBefore = null,
            StatusAfter = request.Status,
            Justification = request.Justification,
        };

    public static AuditLogEntry Approved(Guid accessRequestId, Guid systemOwnerId, string? comment, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            AccessRequestId = accessRequestId,
            Event = AuditEvent.Approved,
            ActorId = systemOwnerId,
            OccurredAt = now,
            StatusBefore = AccessRequestStatus.Pending,
            StatusAfter = AccessRequestStatus.Approved,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
        };

    public static AuditLogEntry Rejected(Guid accessRequestId, Guid systemOwnerId, string rejectionReason, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            AccessRequestId = accessRequestId,
            Event = AuditEvent.Rejected,
            ActorId = systemOwnerId,
            OccurredAt = now,
            StatusBefore = AccessRequestStatus.Pending,
            StatusAfter = AccessRequestStatus.Rejected,
            RejectionReason = rejectionReason,
        };
}
