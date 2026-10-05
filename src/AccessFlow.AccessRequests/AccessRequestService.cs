using System.Data.Common;
using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;

namespace AccessFlow.AccessRequests;

public sealed record CreateAccessRequest(Guid BeneficiaryId, Guid SystemId, string? Justification);

public abstract record CreateAccessRequestResult
{
    public sealed record Created(Guid Id) : CreateAccessRequestResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateAccessRequestResult;

    public sealed record ActiveAccessRequestExists : CreateAccessRequestResult;
}

public sealed record AccessRequestView(
    Guid Id,
    Guid RequesterId,
    Guid BeneficiaryId,
    Guid SystemId,
    string Justification,
    AccessRequestStatus Status,
    DateTimeOffset CreatedAt);

public sealed record AuditLogEntryView(
    Guid AccessRequestId,
    AuditEvent Event,
    Guid? ActorId,
    DateTimeOffset OccurredAt,
    AccessRequestStatus? StatusBefore,
    AccessRequestStatus StatusAfter,
    string? Justification);

public sealed class AccessRequestService(DbContext db, IDirectory directory, TimeProvider timeProvider)
{
    private readonly DbSet<AccessRequest> _accessRequests = db.Set<AccessRequest>();
    private readonly DbSet<AuditLogEntry> _auditLog = db.Set<AuditLogEntry>();

    /// <summary>
    /// Creates a Pending Access Request on behalf of the caller (BR-03…BR-09)
    /// together with its Created Audit Log entry in one transaction (BR-23, BR-28).
    /// </summary>
    public async Task<CreateAccessRequestResult> CreateAsync(Guid callerId, CreateAccessRequest command, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(command.Justification))
            errors[nameof(command.Justification)] = ["Justification is required."];

        if (!await directory.UserExistsAsync(command.BeneficiaryId, cancellationToken))
            errors[nameof(command.BeneficiaryId)] = ["Beneficiary does not exist."];

        if (await directory.FindSystemAsync(command.SystemId, cancellationToken) is null)
            errors[nameof(command.SystemId)] = ["System does not exist."];

        if (errors.Count > 0)
            return new CreateAccessRequestResult.Invalid(errors);

        var request = AccessRequest.Create(
            callerId, command.BeneficiaryId, command.SystemId, command.Justification!, timeProvider.GetUtcNow());

        var created = AuditLogEntry.Created(request);

        _accessRequests.Add(request);
        _auditLog.Add(created);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (ViolatesSingleActiveIndex(e))
        {
            db.Entry(request).State = EntityState.Detached;
            db.Entry(created).State = EntityState.Detached;
            return new CreateAccessRequestResult.ActiveAccessRequestExists();
        }

        return new CreateAccessRequestResult.Created(request.Id);
    }

    // 23505 is PostgreSQL unique_violation. Npgsql also puts the constraint name into DbException.Data,
    // so the module needs no reference to the provider.
    private static bool ViolatesSingleActiveIndex(DbUpdateException e) =>
        e.InnerException is DbException { SqlState: "23505" } dbException
        && dbException.Data["ConstraintName"] as string == AccessRequestConfiguration.SingleActiveIndex;

    /// <summary>
    /// Returns the Access Request, or null when it does not exist or is not visible to the caller (BR-29).
    /// </summary>
    public async Task<AccessRequestView?> GetAsync(Guid callerId, Guid id, CancellationToken cancellationToken)
    {
        var request = await FindVisibleAsync(callerId, id, cancellationToken);
        if (request is null)
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

    /// <summary>
    /// Returns the Audit Log of the Access Request in chronological order,
    /// or null when the Access Request does not exist or is not visible to the caller (BR-29).
    /// </summary>
    public async Task<IReadOnlyList<AuditLogEntryView>?> GetAuditLogAsync(Guid callerId, Guid id, CancellationToken cancellationToken)
    {
        if (await FindVisibleAsync(callerId, id, cancellationToken) is null)
            return null;

        return await _auditLog
            .Where(e => e.AccessRequestId == id)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Sequence)
            .Select(e => new AuditLogEntryView(
                e.AccessRequestId, e.Event, e.ActorId, e.OccurredAt, e.StatusBefore, e.StatusAfter, e.Justification))
            .ToListAsync(cancellationToken);
    }

    private async Task<AccessRequest?> FindVisibleAsync(Guid callerId, Guid id, CancellationToken cancellationToken)
    {
        var request = await _accessRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
            return null;

        var system = await directory.FindSystemAsync(request.SystemId, cancellationToken)
            ?? throw new InvalidOperationException($"System {request.SystemId} of Access Request {request.Id} is missing.");

        return request.IsVisibleTo(callerId, system.OwnerId) ? request : null;
    }
}
