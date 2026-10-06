using System.Net;
using System.Net.Http.Json;
using AccessFlow.AccessRequests;
using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;

namespace AccessFlow.Tests;

/// <summary>
/// BR-16: the approval and its Provisioning outbox entry are saved in one transaction.
/// The shared app runs without the Provisioning worker, so outbox entries stay unprocessed here.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProvisioningOutboxTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => factory.ExecuteSqlAsync(
        $"DROP TRIGGER IF EXISTS fail_outbox_insert ON access_requests.provisioning_outbox; DROP FUNCTION IF EXISTS access_requests.fail_outbox_insert();");

    private sealed record AccessRequestDto(string Status);

    private sealed record AuditLogEntryDto(string Event);

    private sealed record CreatedDto(Guid Id);

    private async Task<Guid> CreateAsync()
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId).PostAsJsonAsync(
            "/access-requests", new { beneficiaryId = DirectorySeed.DaveId, systemId = DirectorySeed.JiraId, justification = "Need it for work" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
    }

    private Task<HttpResponseMessage> ApproveAsync(Guid id) =>
        factory.CreateClientAs(DirectorySeed.AliceId).PostAsync($"/access-requests/{id}/approve", null);

    private Task<HttpResponseMessage> CancelAsync(Guid id) =>
        factory.CreateClientAs(DirectorySeed.CarolId).PostAsync($"/access-requests/{id}/cancel", null);

    private async Task<string> GetStatusAsync(Guid id) =>
        (await factory.CreateClientAs(DirectorySeed.CarolId).GetFromJsonAsync<AccessRequestDto>($"/access-requests/{id}"))!.Status;

    private Task<List<ProvisioningOutboxEntry>> GetOutboxAsync(Guid id) =>
        factory.UseDbAsync(db => db.Set<ProvisioningOutboxEntry>().Where(e => e.AccessRequestId == id).ToListAsync());

    [Fact]
    public async Task Approving_saves_an_unprocessed_outbox_entry_without_calling_the_system()
    {
        var id = await CreateAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await ApproveAsync(id)).StatusCode);

        var entry = Assert.Single(await GetOutboxAsync(id));
        Assert.Null(entry.ProcessedAt);
        Assert.DoesNotContain(factory.System.ReceivedCalls, c => c.Call.AccessRequestId == id);
    }

    [Fact]
    public async Task Approval_is_not_saved_when_its_outbox_entry_cannot_be_saved()
    {
        var id = await CreateAsync();
        await factory.ExecuteSqlAsync($"""
            CREATE FUNCTION access_requests.fail_outbox_insert() RETURNS trigger LANGUAGE plpgsql
                AS 'BEGIN RAISE EXCEPTION ''outbox unavailable''; END';
            CREATE TRIGGER fail_outbox_insert BEFORE INSERT ON access_requests.provisioning_outbox
                FOR EACH ROW EXECUTE FUNCTION access_requests.fail_outbox_insert();
            """);

        // The in-memory test server rethrows the unhandled server error.
        var failure = await Record.ExceptionAsync(() => ApproveAsync(id));
        Assert.NotNull(failure);

        Assert.Equal("Pending", await GetStatusAsync(id));
        var auditLog = await factory.CreateClientAs(DirectorySeed.CarolId).GetFromJsonAsync<AuditLogEntryDto[]>($"/access-requests/{id}/audit-log");
        Assert.Equal(["Created"], auditLog!.Select(e => e.Event));
        Assert.Empty(await GetOutboxAsync(id));
    }

    [Fact]
    public async Task Outbox_entry_is_saved_only_when_the_approval_wins_a_race()
    {
        var id = await CreateAsync();

        // Several of each action widen the race window; exactly one may win.
        await Task.WhenAll(Enumerable.Range(0, 4).SelectMany(_ => new[] { ApproveAsync(id), CancelAsync(id) }));

        var outbox = await GetOutboxAsync(id);
        Assert.Equal(await GetStatusAsync(id) == "Approved" ? 1 : 0, outbox.Count);
    }
}
