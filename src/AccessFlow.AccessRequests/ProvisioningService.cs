using System.Net.Http.Json;
using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessFlow.AccessRequests;

/// <summary>
/// Provisions the Approved Access Requests recorded in the outbox (BR-16…BR-18, BR-22, ADR 0002).
/// Called by the host's background worker, which also configures the <see cref="HttpClient"/>.
/// </summary>
public sealed class ProvisioningService(
    DbContext db, IDirectory directory, HttpClient httpClient, TimeProvider timeProvider, ILogger<ProvisioningService> logger)
{
    private readonly DbSet<AccessRequest> _accessRequests = db.Set<AccessRequest>();
    private readonly DbSet<AuditLogEntry> _auditLog = db.Set<AuditLogEntry>();
    private readonly DbSet<ProvisioningOutboxEntry> _provisioningOutbox = db.Set<ProvisioningOutboxEntry>();

    // BR-17: the same body for every System.
    private sealed record ProvisioningCall(Guid AccessRequestId, Guid BeneficiaryId, Guid SystemId);

    /// <summary>
    /// Makes one attempt for every unprocessed outbox entry, oldest first. The outbox is in the database,
    /// so entries a stopped app left unprocessed are picked up after restart.
    /// </summary>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await _provisioningOutbox
            .Where(e => e.ProcessedAt == null)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.AccessRequestId })
            .ToListAsync(cancellationToken);

        foreach (var entry in pending)
        {
            // Entries are processed independently: a failing one neither blocks the rest
            // nor leaves its unsaved changes behind for their saves.
            db.ChangeTracker.Clear();
            try
            {
                await ProcessAsync(entry.Id, cancellationToken);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(e, "Provisioning of Access Request {AccessRequestId} did not complete; its outbox entry stays unprocessed.", entry.AccessRequestId);
            }
        }
    }

    private async Task ProcessAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await _provisioningOutbox.SingleAsync(e => e.Id == entryId, cancellationToken);
        var request = await _accessRequests.SingleAsync(r => r.Id == entry.AccessRequestId, cancellationToken);

        // BR-17: processing the same Access Request again, e.g. after a failure before the result was saved,
        // changes neither its status nor its Audit Log once Provisioning has completed.
        if (!request.IsApproved)
        {
            entry.MarkProcessed(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var system = await directory.FindSystemAsync(request.SystemId, cancellationToken)
            ?? throw new InvalidOperationException($"System {request.SystemId} of Access Request {request.Id} is missing.");

        // BR-22: every attempt goes to the technical logs, before the call and with its outcome.
        logger.LogInformation("Provisioning attempt for Access Request {AccessRequestId}: POST {Url}.", request.Id, system.ProvisioningUrl);
        using (var response = await httpClient.PostAsJsonAsync(
            system.ProvisioningUrl, new ProvisioningCall(request.Id, request.BeneficiaryId, request.SystemId), cancellationToken))
        {
            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Provisioning attempt for Access Request {AccessRequestId} got {StatusCode}; its outbox entry stays unprocessed.", request.Id, statusCode);
                return;
            }

            logger.LogInformation("Provisioning attempt for Access Request {AccessRequestId} got {StatusCode}.", request.Id, statusCode);
        }

        // BR-18, BR-23, BR-28: the status change, its Audit Log entry and the processed outbox entry are saved in one transaction.
        var now = timeProvider.GetUtcNow();
        _auditLog.Add(request.CompleteProvisioning(now));
        entry.MarkProcessed(now);
        await db.SaveChangesAsync(cancellationToken);
    }
}
