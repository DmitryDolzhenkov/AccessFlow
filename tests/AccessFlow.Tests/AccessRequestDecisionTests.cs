using System.Net;
using System.Net.Http.Json;
using AccessFlow.Directory;

namespace AccessFlow.Tests;

/// <summary>
/// BR-10…BR-13, BR-15, BR-27, BR-30: System Owner approves or rejects an Access Request.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccessRequestDecisionTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    // Restores the seeded System Owner that a test may have changed.
    public Task DisposeAsync() => SetSystemOwnerAsync(DirectorySeed.JiraId, DirectorySeed.AliceId);

    private sealed record AuditLogEntryDto(
        string Event,
        Guid? ActorId,
        string? StatusBefore,
        string StatusAfter,
        string? Comment,
        string? RejectionReason);

    private sealed record AccessRequestDto(string Status);

    private sealed record CreatedDto(Guid Id);

    private static readonly string[] Decisions = ["approve", "reject"];

    private Task SetSystemOwnerAsync(Guid systemId, Guid ownerId) =>
        factory.ExecuteSqlAsync($"UPDATE directory.systems SET \"OwnerId\" = {ownerId} WHERE \"Id\" = {systemId}");

    private async Task<Guid> CreateAsync(Guid requesterId, Guid beneficiaryId, Guid systemId)
    {
        var response = await factory.CreateClientAs(requesterId)
            .PostAsJsonAsync("/access-requests", new { beneficiaryId, systemId, justification = "Need it for work" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
    }

    private Task<HttpResponseMessage> PostDecisionAsync(string decision, Guid callerId, Guid id, object? body) =>
        body is null
            ? factory.CreateClientAs(callerId).PostAsync($"/access-requests/{id}/{decision}", null)
            : factory.CreateClientAs(callerId).PostAsJsonAsync($"/access-requests/{id}/{decision}", body);

    private Task<HttpResponseMessage> ApproveAsync(Guid callerId, Guid id, object? body) =>
        PostDecisionAsync("approve", callerId, id, body);

    private Task<HttpResponseMessage> RejectAsync(Guid callerId, Guid id, object? body) =>
        PostDecisionAsync("reject", callerId, id, body);

    // A valid approve or reject, so a test can cover both decisions.
    private Task<HttpResponseMessage> DecideAsync(string decision, Guid callerId, Guid id) =>
        decision == "approve"
            ? ApproveAsync(callerId, id, body: null)
            : RejectAsync(callerId, id, new { rejectionReason = "Not needed" });

    private async Task<string> GetStatusAsync(Guid viewerId, Guid id) =>
        (await factory.CreateClientAs(viewerId).GetFromJsonAsync<AccessRequestDto>($"/access-requests/{id}"))!.Status;

    private async Task<AuditLogEntryDto[]> GetAuditLogAsync(Guid viewerId, Guid id) =>
        (await factory.CreateClientAs(viewerId).GetFromJsonAsync<AuditLogEntryDto[]>($"/access-requests/{id}/audit-log"))!;

    // BR-27: a refused attempt changes nothing and leaves no Audit Log entry.
    private async Task AssertStillPendingWithoutDecisionAsync(Guid id)
    {
        Assert.Equal("Pending", await GetStatusAsync(DirectorySeed.CarolId, id));
        var entry = Assert.Single(await GetAuditLogAsync(DirectorySeed.CarolId, id));
        Assert.Equal("Created", entry.Event);
    }

    [Fact]
    public async Task System_owner_approves_a_pending_access_request_with_a_comment()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await ApproveAsync(DirectorySeed.AliceId, id, new { comment = "Welcome aboard" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Approved", await GetStatusAsync(DirectorySeed.CarolId, id));
        var auditLog = await GetAuditLogAsync(DirectorySeed.CarolId, id);
        Assert.Equal(["Created", "Approved"], auditLog.Select(e => e.Event));
        Assert.Equal(
            new AuditLogEntryDto("Approved", DirectorySeed.AliceId, "Pending", "Approved", "Welcome aboard", null),
            auditLog[1]);
    }

    [Fact]
    public async Task System_owner_approves_without_a_comment()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await ApproveAsync(DirectorySeed.AliceId, id, body: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var approved = (await GetAuditLogAsync(DirectorySeed.CarolId, id))[1];
        Assert.Equal(new AuditLogEntryDto("Approved", DirectorySeed.AliceId, "Pending", "Approved", null, null), approved);
    }

    [Fact]
    public async Task System_owner_rejects_a_pending_access_request_with_a_rejection_reason()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await RejectAsync(DirectorySeed.AliceId, id, new { rejectionReason = "Use Confluence instead" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Rejected", await GetStatusAsync(DirectorySeed.CarolId, id));
        var auditLog = await GetAuditLogAsync(DirectorySeed.CarolId, id);
        Assert.Equal(["Created", "Rejected"], auditLog.Select(e => e.Event));
        Assert.Equal(
            new AuditLogEntryDto("Rejected", DirectorySeed.AliceId, "Pending", "Rejected", null, "Use Confluence instead"),
            auditLog[1]);
    }

    public static TheoryData<string?> MissingRejectionReasonBodies => new()
    {
        null, // no body at all
        "{}",
        """{ "rejectionReason": null }""",
        """{ "rejectionReason": "" }""",
        """{ "rejectionReason": "   " }""",
    };

    [Theory]
    [MemberData(nameof(MissingRejectionReasonBodies))]
    public async Task Rejecting_without_a_rejection_reason_returns_400(string? json)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var content = json is null ? null : new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await factory.CreateClientAs(DirectorySeed.AliceId).PostAsync($"/access-requests/{id}/reject", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertStillPendingWithoutDecisionAsync(id);
    }

    [Theory]
    [InlineData(true)] // System Owner requests for themselves
    [InlineData(false)] // someone else requests for the System Owner
    public async Task System_owner_decides_an_access_request_where_they_are_requester_or_beneficiary(bool ownerIsRequester)
    {
        var id = ownerIsRequester
            ? await CreateAsync(DirectorySeed.AliceId, DirectorySeed.AliceId, DirectorySeed.JiraId)
            : await CreateAsync(DirectorySeed.CarolId, DirectorySeed.AliceId, DirectorySeed.JiraId);

        var response = await ApproveAsync(DirectorySeed.AliceId, id, body: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Approved", await GetStatusAsync(DirectorySeed.AliceId, id));
    }

    [Fact]
    public async Task Only_the_current_system_owner_decides_after_the_owner_changes()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        await SetSystemOwnerAsync(DirectorySeed.JiraId, DirectorySeed.BobId);

        var byFormerOwner = await ApproveAsync(DirectorySeed.AliceId, id, body: null);
        Assert.Equal(HttpStatusCode.NotFound, byFormerOwner.StatusCode);
        await AssertStillPendingWithoutDecisionAsync(id);

        var byCurrentOwner = await ApproveAsync(DirectorySeed.BobId, id, body: null);
        Assert.Equal(HttpStatusCode.NoContent, byCurrentOwner.StatusCode);
        Assert.Equal(DirectorySeed.BobId, (await GetAuditLogAsync(DirectorySeed.CarolId, id))[1].ActorId);
    }

    public static TheoryData<string, string> SecondDecisions => new()
    {
        { "approve", "approve" },
        { "approve", "reject" },
        { "reject", "approve" },
        { "reject", "reject" },
    };

    [Theory]
    [MemberData(nameof(SecondDecisions))]
    public async Task Deciding_an_access_request_that_is_not_pending_returns_409(string first, string second)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        Assert.Equal(HttpStatusCode.NoContent, (await DecideAsync(first, DirectorySeed.AliceId, id)).StatusCode);
        var statusAfterFirst = await GetStatusAsync(DirectorySeed.CarolId, id);

        var response = await DecideAsync(second, DirectorySeed.AliceId, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(statusAfterFirst, await GetStatusAsync(DirectorySeed.CarolId, id));
        Assert.Equal(2, (await GetAuditLogAsync(DirectorySeed.CarolId, id)).Length);
    }

    [Fact]
    public async Task Concurrent_decisions_apply_one_and_return_409_for_the_others()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        // Several of each decision widen the race window; exactly one may win.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).SelectMany(_ => Decisions).Select(d => DecideAsync(d, DirectorySeed.AliceId, id)));

        Assert.Equal(
            [HttpStatusCode.NoContent, .. Enumerable.Repeat(HttpStatusCode.Conflict, responses.Length - 1)],
            responses.Select(r => r.StatusCode).Order());
        var auditLog = await GetAuditLogAsync(DirectorySeed.CarolId, id);
        Assert.Equal(2, auditLog.Length);
        Assert.Equal(auditLog[1].StatusAfter, await GetStatusAsync(DirectorySeed.CarolId, id));
    }

    public static TheoryData<Guid> RequesterAndBeneficiaryOfCarolsRequestForDaveToJira => new()
    {
        DirectorySeed.CarolId, // Requester
        DirectorySeed.DaveId, // Beneficiary
    };

    [Theory]
    [MemberData(nameof(RequesterAndBeneficiaryOfCarolsRequestForDaveToJira))]
    public async Task Requester_or_beneficiary_who_is_not_the_system_owner_gets_403(Guid callerId)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        foreach (var decision in Decisions)
        {
            var response = await DecideAsync(decision, callerId, id);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await AssertStillPendingWithoutDecisionAsync(id);
        }
    }

    [Fact]
    public async Task User_who_does_not_see_the_access_request_gets_404()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        foreach (var decision in Decisions)
        {
            var response = await DecideAsync(decision, DirectorySeed.BobId, id);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertStillPendingWithoutDecisionAsync(id);
        }
    }

    [Fact]
    public async Task Deciding_an_unknown_access_request_returns_404()
    {
        foreach (var decision in Decisions)
        {
            var response = await DecideAsync(decision, DirectorySeed.AliceId, Guid.NewGuid());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
