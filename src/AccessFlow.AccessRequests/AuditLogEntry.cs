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
    public Guid AccessRequestId { get; private init; }
    public AuditEvent Event { get; private init; }
    public Guid ActorId { get; private init; }
    public DateTimeOffset OccurredAt { get; private init; }
    public AccessRequestStatus? StatusBefore { get; private init; }
    public AccessRequestStatus StatusAfter { get; private init; }
    public string? Justification { get; private init; }

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
}
