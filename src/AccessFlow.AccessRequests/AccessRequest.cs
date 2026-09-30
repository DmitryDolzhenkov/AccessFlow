namespace AccessFlow.AccessRequests;

public enum AccessRequestStatus
{
    Pending,
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
}
