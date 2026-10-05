namespace AccessFlow.AccessRequests;

public enum AccessRequestStatus
{
    Pending,
    Approved,
    Provisioned,
    ProvisioningFailed,
    Rejected,
    Cancelled,
}

internal sealed class AccessRequest
{
    private AccessRequest()
    {
    }

    public Guid Id { get; private init; }
    public Guid RequesterId { get; private init; }
    public Guid BeneficiaryId { get; private init; }
    public Guid SystemId { get; private init; }
    public string Justification { get; private init; } = null!;
    public AccessRequestStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    public static AccessRequest Create(Guid requesterId, Guid beneficiaryId, Guid systemId, string justification, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            RequesterId = requesterId,
            BeneficiaryId = beneficiaryId,
            SystemId = systemId,
            Justification = justification,
            Status = AccessRequestStatus.Pending,
            CreatedAt = now,
        };

    public bool IsVisibleTo(Guid userId, Guid systemOwnerId) =>
        userId == RequesterId || userId == BeneficiaryId || userId == systemOwnerId;

    // BR-10, BR-11: only the current System Owner decides, even on their own Access Request.
    public bool CanBeDecidedBy(Guid userId, Guid systemOwnerId) => userId == systemOwnerId;

    public bool IsPending => Status == AccessRequestStatus.Pending;

    public AuditLogEntry Approve(Guid systemOwnerId, string? comment, DateTimeOffset now)
    {
        EnsurePending();
        Status = AccessRequestStatus.Approved;
        return AuditLogEntry.Approved(Id, systemOwnerId, comment, now);
    }

    public AuditLogEntry Reject(Guid systemOwnerId, string rejectionReason, DateTimeOffset now)
    {
        EnsurePending();
        Status = AccessRequestStatus.Rejected;
        return AuditLogEntry.Rejected(Id, systemOwnerId, rejectionReason, now);
    }

    // BR-12: callers check IsPending first; this guards the invariant.
    private void EnsurePending()
    {
        if (!IsPending)
            throw new InvalidOperationException($"Access Request {Id} is {Status}, not {AccessRequestStatus.Pending}.");
    }
}
