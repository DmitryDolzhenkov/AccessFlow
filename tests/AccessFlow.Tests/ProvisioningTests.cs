using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using AccessFlow.AccessRequests;
using AccessFlow.Directory;
using AccessFlow.SystemStub;
using Microsoft.EntityFrameworkCore;

namespace AccessFlow.Tests;

/// <summary>
/// BR-17…BR-25, BR-28: the background worker provisions Approved Access Requests, retrying transient failures.
/// API calls go to the shared app; Provisioning runs in an instance started on the same database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProvisioningTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => factory.ExecuteSqlAsync(
        $"DROP TRIGGER IF EXISTS fail_provisioned_entry ON access_requests.audit_log; DROP FUNCTION IF EXISTS access_requests.fail_provisioned_entry();");

    private sealed record AccessRequestDto(string Status);

    private sealed record AuditLogEntryDto(string Event, Guid? ActorId, string? StatusBefore, string StatusAfter, string? ProvisioningFailureReason = null);

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

    private Task WaitUntilProvisioningFailedAsync(Guid id) =>
        WaitUntilAsync(async () => await GetStatusAsync(id) == "ProvisioningFailed", "the Access Request is ProvisioningFailed");

    private static AuditLogEntryDto ProvisioningFailedEntry(string reason) => new("ProvisioningFailed", ActorId: null, "Approved", "ProvisioningFailed", reason);

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

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task System_answering_4xx_fails_provisioning_without_retries(HttpStatusCode answer)
    {
        var system = new ScriptedSystem(factory.System, Status(answer));
        await using var app = factory.StartWithProvisioning(() => system);

        var id = await CreateAndApproveAsync();

        await WaitUntilProvisioningFailedAsync(id);
        Assert.Equal(1, system.CallCount);
        Assert.Empty(CallsFor(id));
        var auditLog = await GetAuditLogAsync(id);
        Assert.Equal(["Created", "Approved", "ProvisioningFailed"], auditLog.Select(e => e.Event));
        Assert.Equal(ProvisioningFailedEntry($"The System answered {(int)answer}."), auditLog[2]);
    }

    public static TheoryData<string, string> TransientFailures => new()
    {
        { "500", "3 attempts failed. The last one: The System answered 500." },
        { "503", "3 attempts failed. The last one: The System answered 503." },
        { "timeout", "3 attempts failed. The last one: The System did not answer within 00:00:01." },
        { "no response", "3 attempts failed. The last one: The System could not be reached." },
    };

    private static Answer TransientFailure(string name) => name switch
    {
        "timeout" => Hang,
        "no response" => NoResponse,
        _ => Status((HttpStatusCode)int.Parse(name)),
    };

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task Provisioning_fails_when_every_attempt_fails_transiently(string failure, string reason)
    {
        var answer = TransientFailure(failure);
        var system = new ScriptedSystem(factory.System, answer, answer, answer);
        await using var app = factory.StartWithProvisioning(() => system);

        var id = await CreateAndApproveAsync();

        await WaitUntilProvisioningFailedAsync(id);
        Assert.Equal(3, system.CallCount);
        Assert.Empty(CallsFor(id));
        var auditLog = await GetAuditLogAsync(id);
        Assert.Equal(["Created", "Approved", "ProvisioningFailed"], auditLog.Select(e => e.Event));
        Assert.Equal(ProvisioningFailedEntry(reason), auditLog[2]);
    }

    [Theory]
    [InlineData("500", "503")]
    [InlineData("timeout", "no response")]
    public async Task Access_request_is_provisioned_when_a_retry_succeeds(string firstFailure, string secondFailure)
    {
        var system = new ScriptedSystem(factory.System, TransientFailure(firstFailure), TransientFailure(secondFailure));
        await using var app = factory.StartWithProvisioning(() => system);

        var id = await CreateAndApproveAsync();

        await WaitUntilProvisionedAsync(id);
        Assert.Equal(3, system.CallCount);
        Assert.Single(CallsFor(id));
        var auditLog = await GetAuditLogAsync(id);
        Assert.Equal(["Created", "Approved", "Provisioned"], auditLog.Select(e => e.Event));
        Assert.Equal(ProvisionedEntry, auditLog[2]);
    }

    [Fact]
    public async Task Each_retry_waits_twice_as_long_as_the_previous_one()
    {
        var system = new ScriptedSystem(factory.System, Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable));
        await using var app = factory.StartWithProvisioning(() => system);

        var id = await CreateAndApproveAsync();

        await WaitUntilProvisionedAsync(id);
        // Provisioning:BaseDelay is 100 ms in the tests.
        Assert.Collection(
            system.Gaps,
            gap => Assert.True(gap >= TimeSpan.FromMilliseconds(100), $"The first retry came after {gap}."),
            gap => Assert.True(gap >= TimeSpan.FromMilliseconds(200), $"The second retry came after {gap}."));
    }

    [Fact]
    public async Task After_provisioning_failed_only_a_new_access_request_can_try_again()
    {
        await using var app = factory.StartWithProvisioning(() => new ScriptedSystem(factory.System, Status(HttpStatusCode.NotFound)));
        var id = await CreateAndApproveAsync();
        await WaitUntilProvisioningFailedAsync(id);

        // BR-21: the failed Access Request cannot be approved again.
        var approvedAgain = await factory.CreateClientAs(DirectorySeed.AliceId).PostAsync($"/access-requests/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, approvedAgain.StatusCode);

        // BR-09: ProvisioningFailed is final, so the same Beneficiary + System may have a new Active Access Request.
        var newId = await CreateAndApproveAsync();
        await WaitUntilProvisionedAsync(newId);
        Assert.Equal("ProvisioningFailed", await GetStatusAsync(id));
    }

    [Fact]
    public async Task Each_failed_provisioning_attempt_is_logged()
    {
        var logs = new CapturedLogs();
        await using var app = factory.StartWithProvisioning(
            () => new ScriptedSystem(factory.System, Status(HttpStatusCode.ServiceUnavailable), Hang, NoResponse), logs);

        var id = await CreateAndApproveAsync();

        await WaitUntilProvisioningFailedAsync(id);
        var attemptLogs = logs.Entries
            .Where(e => e.Category == typeof(ProvisioningService).FullName && Equals(e.Properties.GetValueOrDefault("AccessRequestId"), id))
            .ToList();
        Assert.Equal([1, 2, 3], attemptLogs.Select(e => e.Properties.GetValueOrDefault("Attempt")).OfType<int>());
        Assert.Contains(attemptLogs, e => Equals(e.Properties.GetValueOrDefault("StatusCode"), 503));
        Assert.Contains(attemptLogs, e => Equals(e.Properties.GetValueOrDefault("Timeout"), TimeSpan.FromSeconds(1)));
        Assert.Contains(attemptLogs, e => Equals(e.Properties.GetValueOrDefault("Reason"), "3 attempts failed. The last one: The System could not be reached."));
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

    private delegate Task<HttpResponseMessage> Answer(CancellationToken cancellationToken);

    private static Answer Status(HttpStatusCode code) => _ => Task.FromResult(new HttpResponseMessage(code));

    // Never answers, so the call times out.
    private static readonly Answer Hang = async cancellationToken =>
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new UnreachableException();
    };

    // The call gets no HTTP response, as when the connection is refused.
    private static readonly Answer NoResponse = _ => throw new HttpRequestException("Connection refused.");

    /// <summary>
    /// Answers each call with the next scripted answer without granting access; once the script runs out,
    /// calls reach the System stub, which grants access.
    /// </summary>
    private sealed class ScriptedSystem : DelegatingHandler
    {
        private readonly ConcurrentQueue<Answer> _answers;
        private readonly ConcurrentQueue<long> _callTimestamps = new();

        public ScriptedSystem(SystemStubHost stub, params Answer[] answers)
        {
            _answers = new ConcurrentQueue<Answer>(answers);
            InnerHandler = stub.CreateHandler();
        }

        public int CallCount => _callTimestamps.Count;

        // The time between consecutive calls.
        public IReadOnlyList<TimeSpan> Gaps =>
            _callTimestamps.Zip(_callTimestamps.Skip(1), (previous, next) => Stopwatch.GetElapsedTime(previous, next)).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callTimestamps.Enqueue(Stopwatch.GetTimestamp());
            return _answers.TryDequeue(out var answer)
                ? await answer(cancellationToken)
                : await base.SendAsync(request, cancellationToken);
        }
    }
}
