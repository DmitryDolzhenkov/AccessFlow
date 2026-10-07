using System.Net.Http.Json;
using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AccessFlow.AccessRequests;

/// <summary>
/// Provisions the Approved Access Requests recorded in the outbox (BR-16…BR-22, ADR 0002).
/// Called by the host's background worker; the host also configures the <see cref="HttpClient"/> with <see cref="ProvisioningOptions.Timeout"/>.
/// </summary>
public sealed class ProvisioningService(
    DbContext db,
    IDirectory directory,
    HttpClient httpClient,
    ProvisioningOptions options,
    TimeProvider timeProvider,
    ILogger<ProvisioningService> logger)
{
    private readonly DbSet<AccessRequest> _accessRequests = db.Set<AccessRequest>();
    private readonly DbSet<AuditLogEntry> _auditLog = db.Set<AuditLogEntry>();
    private readonly DbSet<ProvisioningOutboxEntry> _provisioningOutbox = db.Set<ProvisioningOutboxEntry>();

    // BR-17: the same body for every System.
    private sealed record ProvisioningCall(Guid AccessRequestId, Guid BeneficiaryId, Guid SystemId);

    // Why an attempt failed and whether another attempt may succeed (BR-19, BR-20).
    private sealed record AttemptFailure(string Reason, bool IsTransient);

    /// <summary>
    /// Makes one attempt for every unprocessed outbox entry that is due, oldest first. The outbox is in the database,
    /// so entries a stopped app left unprocessed, including scheduled retries, are picked up after restart.
    /// </summary>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var pending = await _provisioningOutbox
            .Where(e => e.ProcessedAt == null && e.NextAttemptAt <= now)
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
        var attempt = entry.AttemptCount + 1;
        logger.LogInformation(
            "Provisioning attempt {Attempt} of {MaxAttempts} for Access Request {AccessRequestId}: POST {Url}.",
            attempt, options.MaxAttempts, request.Id, system.ProvisioningUrl);
        var failure = await CallSystemAsync(system.ProvisioningUrl, request, cancellationToken);

        // BR-23, BR-28: a status change, its Audit Log entry and the outbox entry are saved in one transaction.
        var now = timeProvider.GetUtcNow();
        if (failure is null)
        {
            // BR-18.
            _auditLog.Add(request.CompleteProvisioning(now));
            entry.MarkProcessedAfterAttempt(now);
        }
        else if (failure.IsTransient && attempt < options.MaxAttempts)
        {
            // BR-20: retried with exponential backoff.
            entry.ScheduleRetry(now, options.BaseDelay);
            logger.LogWarning(
                "Provisioning of Access Request {AccessRequestId} will be retried at {NextAttemptAt}.", request.Id, entry.NextAttemptAt);
        }
        else
        {
            // BR-19: a permanent failure is final at once; BR-20: a transient one once the attempts are exhausted.
            var reason = failure.IsTransient ? $"{attempt} attempts failed. The last one: {failure.Reason}" : failure.Reason;
            _auditLog.Add(request.FailProvisioning(reason, now));
            entry.MarkProcessedAfterAttempt(now);
            logger.LogWarning("Provisioning of Access Request {AccessRequestId} failed: {Reason}", request.Id, reason);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // Returns null when the System granted access.
    private async Task<AttemptFailure?> CallSystemAsync(string url, AccessRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                url, new ProvisioningCall(request.Id, request.BeneficiaryId, request.SystemId), cancellationToken);
            var statusCode = (int)response.StatusCode;
            logger.Log(
                response.IsSuccessStatusCode ? LogLevel.Information : LogLevel.Warning,
                "Provisioning attempt for Access Request {AccessRequestId} got {StatusCode}.", request.Id, statusCode);

            // BR-20: 5xx is transient; BR-19: 4xx, like any other answer that is not 2xx, is permanent.
            return response.IsSuccessStatusCode
                ? null
                : new AttemptFailure($"The System answered {statusCode}.", IsTransient: statusCode >= 500);
        }
        catch (TaskCanceledException e) when (e.InnerException is TimeoutException)
        {
            // BR-20: HttpClient.Timeout elapsed.
            logger.LogWarning(
                "Provisioning attempt for Access Request {AccessRequestId} got no answer within {Timeout}.", request.Id, httpClient.Timeout);
            return new AttemptFailure($"The System did not answer within {httpClient.Timeout}.", IsTransient: true);
        }
        catch (HttpRequestException e)
        {
            // No answer at all, e.g. the connection was refused: retried like 5xx.
            logger.LogWarning(e, "Provisioning attempt for Access Request {AccessRequestId} could not reach the System.", request.Id);
            return new AttemptFailure("The System could not be reached.", IsTransient: true);
        }
    }
}
