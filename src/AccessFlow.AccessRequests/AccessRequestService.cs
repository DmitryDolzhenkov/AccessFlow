using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;

namespace AccessFlow.AccessRequests;

public sealed record CreateAccessRequest(Guid BeneficiaryId, Guid SystemId, string? Justification);

public abstract record CreateAccessRequestResult
{
    public sealed record Created(Guid Id) : CreateAccessRequestResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateAccessRequestResult;
}

public sealed record AccessRequestView(
    Guid Id,
    Guid RequesterId,
    Guid BeneficiaryId,
    Guid SystemId,
    string Justification,
    AccessRequestStatus Status,
    DateTimeOffset CreatedAt);

public sealed class AccessRequestService
{
    private readonly AccessRequestsDbContext _db;
    private readonly IDirectory _directory;
    private readonly TimeProvider _timeProvider;

    internal AccessRequestService(AccessRequestsDbContext db, IDirectory directory, TimeProvider timeProvider)
    {
        _db = db;
        _directory = directory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Creates a Pending Access Request on behalf of the caller (BR-03…BR-07).
    /// </summary>
    public async Task<CreateAccessRequestResult> CreateAsync(Guid callerId, CreateAccessRequest command, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.Justification))
            errors[nameof(command.Justification)] = ["Justification is required."];

        if (!await _directory.UserExistsAsync(command.BeneficiaryId, cancellationToken))
            errors[nameof(command.BeneficiaryId)] = ["Beneficiary does not exist."];

        if (await _directory.FindSystemAsync(command.SystemId, cancellationToken) is null)
            errors[nameof(command.SystemId)] = ["System does not exist."];

        if (errors.Count > 0)
            return new CreateAccessRequestResult.Invalid(errors);

        var request = AccessRequest.Create(
            callerId, command.BeneficiaryId, command.SystemId, command.Justification!, _timeProvider.GetUtcNow());

        _db.AccessRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken);

        return new CreateAccessRequestResult.Created(request.Id);
    }

    /// <summary>
    /// Returns the Access Request, or null when it does not exist or is not visible to the caller (BR-29).
    /// </summary>
    public async Task<AccessRequestView?> GetAsync(Guid callerId, Guid id, CancellationToken cancellationToken)
    {
        var request = await _db.AccessRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
            return null;

        var system = await _directory.FindSystemAsync(request.SystemId, cancellationToken)
            ?? throw new InvalidOperationException($"System {request.SystemId} of Access Request {request.Id} is missing.");

        if (!request.IsVisibleTo(callerId, system.OwnerId))
            return null;

        return new AccessRequestView(
            request.Id,
            request.RequesterId,
            request.BeneficiaryId,
            request.SystemId,
            request.Justification,
            request.Status,
            request.CreatedAt);
    }
}
