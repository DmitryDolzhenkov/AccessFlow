using System.Net;
using System.Net.Http.Json;
using AccessFlow.AccessRequests;
using AccessFlow.Directory;
using AccessFlow.SystemStub;
using Microsoft.EntityFrameworkCore;

namespace AccessFlow.Tests;

/// <summary>
/// BR-17, BR-18, BR-22, BR-23, BR-25, BR-28: the background worker provisions Approved Access Requests.
/// API calls go to the shared app; Provisioning runs in an instance started on the same database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProvisioningTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => factory.ExecuteSqlAsync(
        $"DROP TRIGGER IF EXISTS fail_provisioned_entry ON access_requests.audit_log; DROP FUNCTION IF EXISTS access_requests.fail_provisioned_entry();");

    private sealed record AccessRequestDto(string Status);

    private sealed record AuditLogEntryDto(string Event, Guid? ActorId, string? StatusBefore, string StatusAfter);

    private sealed record CreatedDto(Guid Id);

    private async Task<Guid> CreateAndApproveAsync()
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId).PostAsJsonAsync(
            "/access-requests", new { beneficiaryId = DirectorySeed.DaveId, systemId = DirectorySeed.JiraId, justification = "Need it for work" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        var approved = await factory.CreateClientAs(DirectorySeed.AliceId).PostAsync($"/access-requests/{id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approved.StatusCode);
        return id;
    }

    private async Task<string> GetStatusAsync(Guid id) =>
        (await factory.CreateClientAs(DirectorySeed.CarolId).GetFromJsonAsync<AccessRequestDto>($"/access-requests/{id}"))!.Status;

    private async Task<AuditLogEntryDto[]> GetAuditLogAsync(Guid id) =>
        (await factory.CreateClientAs(DirectorySeed.CarolId).GetFromJsonAsync<AuditLogEntryDto[]>($"/access-requests/{id}/audit-log"))!;

    private Task<ProvisioningOutboxEntry> GetOutboxEntryAsync(Guid id) =>
        factory.UseDbAsync(db => db.Set<ProvisioningOutboxEntry>().SingleAsync(e => e.AccessRequestId == id));

    private IEnumerable<ReceivedProvisioningCall> CallsFor(Guid id) =>
        factory.System.ReceivedCalls.Where(c => c.Call.AccessRequestId == id);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting until {description}.");
            await Task.Delay(50);
        }
    }

    private Task WaitUntilProvisionedAsync(Guid id) =>
        WaitUntilAsync(async () => await GetStatusAsync(id) == "Provisioned", "the Access Request is Provisioned");

    private static readonly AuditLogEntryDto ProvisionedEntry = new("Provisioned", ActorId: null, "Approved", "Provisioned");

    [Fact]
    public async Task Approved_access_request_is_provisioned_through_its_system()
    {
        await using var app = factory.StartWithProvisioning();

        var id = await CreateAndApproveAsync();

        await WaitUntilProvisionedAsync(id);
        Assert.Equal(
            [new ReceivedProvisioningCall("jira", new ProvisioningCall(id, DirectorySeed.DaveId, DirectorySeed.JiraId))],
            CallsFor(id));
        var auditLog = await GetAuditLogAsync(id);
        Assert.Equal(["Created", "Approved", "Provisioned"], auditLog.Select(e => e.Event));
        Assert.Equal(ProvisionedEntry, auditLog[2]);
        Assert.NotNull((await GetOutboxEntryAsync(id)).ProcessedAt);
    }

    [Fact]
    public async Task Provisioned_status_is_not_saved_without_its_audit_log_entry()
    {
        await factory.ExecuteSqlAsync($"""
            CREATE FUNCTION access_requests.fail_provisioned_entry() RETURNS trigger LANGUAGE plpgsql
                AS 'BEGIN RAISE EXCEPTION ''audit log unavailable''; END';
            CREATE TRIGGER fail_provisioned_entry BEFORE INSERT ON access_requests.audit_log
                FOR EACH ROW WHEN (NEW."Event" = 'Provisioned') EXECUTE FUNCTION access_requests.fail_provisioned_entry();
            """);
        await using var app = factory.StartWithProvisioning();
        var id = await CreateAndApproveAsync();

        // A second call means the first attempt's result failed to save.
        await WaitUntilAsync(() => Task.FromResult(CallsFor(id).Count() >= 2), "the System is called again");
        Assert.Equal("Approved", await GetStatusAsync(id));
        Assert.Equal(["Created", "Approved"], (await GetAuditLogAsync(id)).Select(e => e.Event));
        Assert.Null((await GetOutboxEntryAsync(id)).ProcessedAt);

        await factory.ExecuteSqlAsync($"DROP TRIGGER fail_provisioned_entry ON access_requests.audit_log");
        await WaitUntilProvisionedAsync(id);
        Assert.Equal(["Created", "Approved", "Provisioned"], (await GetAuditLogAsync(id)).Select(e => e.Event));
    }

    [Fact]
    public async Task Provisioning_interrupted_by_a_restart_completes_once_after_it()
    {
        // The first instance stops after the System granted access but before the result was saved.
        var granted = new TaskCompletionSource();
        var first = factory.StartWithProvisioning(() => new UnansweredCall(granted) { InnerHandler = factory.System.CreateHandler() });
        var id = await CreateAndApproveAsync();
        await granted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await first.DisposeAsync();
        Assert.Equal("Approved", await GetStatusAsync(id));

        await using var restarted = factory.StartWithProvisioning();

        await WaitUntilProvisionedAsync(id);
        Assert.Equal(2, CallsFor(id).Count());
        var auditLog = await GetAuditLogAsync(id);
        Assert.Equal(["Created", "Approved", "Provisioned"], auditLog.Select(e => e.Event));
        Assert.Equal(ProvisionedEntry, auditLog[2]);
    }

    [Fact]
    public async Task Processing_an_already_processed_entry_again_changes_nothing()
    {
        await using var app = factory.StartWithProvisioning();
        var id = await CreateAndApproveAsync();
        await WaitUntilProvisionedAsync(id);

        await factory.ExecuteSqlAsync(
            $"UPDATE access_requests.provisioning_outbox SET \"ProcessedAt\" = NULL WHERE \"AccessRequestId\" = {id}");
        await WaitUntilAsync(async () => (await GetOutboxEntryAsync(id)).ProcessedAt is not null, "the entry is processed again");

        Assert.Equal("Provisioned", await GetStatusAsync(id));
        Assert.Equal(["Created", "Approved", "Provisioned"], (await GetAuditLogAsync(id)).Select(e => e.Event));
        Assert.Single(CallsFor(id));
    }

    [Fact]
    public async Task Each_provisioning_attempt_is_logged()
    {
        var logs = new CapturedLogs();
        await using var app = factory.StartWithProvisioning(logs: logs);

        var id = await CreateAndApproveAsync();

        await WaitUntilProvisionedAsync(id);
        var attemptLogs = logs.Entries
            .Where(e => e.Category == typeof(ProvisioningService).FullName && Equals(e.Properties.GetValueOrDefault("AccessRequestId"), id))
            .ToList();
        Assert.Contains(attemptLogs, e => Equals(e.Properties.GetValueOrDefault("Url"), "http://localhost:5199/provisioning/jira"));
        Assert.Contains(attemptLogs, e => Equals(e.Properties.GetValueOrDefault("StatusCode"), 200));
    }

    /// <summary>
    /// Delivers the call to the System but never returns its answer, as when the app stops mid-call.
    /// </summary>
    private sealed class UnansweredCall(TaskCompletionSource delivered) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (await base.SendAsync(request, cancellationToken)).Dispose();
            delivered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
