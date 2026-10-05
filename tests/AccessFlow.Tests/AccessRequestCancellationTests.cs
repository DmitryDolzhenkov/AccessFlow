using System.Net;
using System.Net.Http.Json;
using AccessFlow.Directory;

namespace AccessFlow.Tests;

/// <summary>
/// BR-14, BR-15, BR-27, BR-30: Requester cancels their Access Request.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccessRequestCancellationTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record AuditLogEntryDto(
        string Event,
        Guid? ActorId,
        string? StatusBefore,
        string StatusAfter,
        string? Justification,
        string? Comment,
        string? RejectionReason);

    private sealed record AccessRequestDto(string Status);

    private sealed record CreatedDto(Guid Id);

    private async Task<Guid> CreateAsync(Guid requesterId, Guid beneficiaryId, Guid systemId)
    {
        var response = await factory.CreateClientAs(requesterId)
            .PostAsJsonAsync("/access-requests", new { beneficiaryId, systemId, justification = "Need it for work" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
    }

    private Task<HttpResponseMessage> CancelAsync(Guid callerId, Guid id) =>
        factory.CreateClientAs(callerId).PostAsync($"/access-requests/{id}/cancel", null);

    private Task<HttpResponseMessage> ApproveAsync(Guid callerId, Guid id) =>
        factory.CreateClientAs(callerId).PostAsync($"/access-requests/{id}/approve", null);

    private Task<HttpResponseMessage> RejectAsync(Guid callerId, Guid id) =>
        factory.CreateClientAs(callerId).PostAsJsonAsync($"/access-requests/{id}/reject", new { rejectionReason = "Not needed" });

    private async Task<string> GetStatusAsync(Guid viewerId, Guid id) =>
        (await factory.CreateClientAs(viewerId).GetFromJsonAsync<AccessRequestDto>($"/access-requests/{id}"))!.Status;

    private async Task<AuditLogEntryDto[]> GetAuditLogAsync(Guid viewerId, Guid id) =>
        (await factory.CreateClientAs(viewerId).GetFromJsonAsync<AuditLogEntryDto[]>($"/access-requests/{id}/audit-log"))!;

    // BR-27: a refused attempt changes nothing and leaves no Audit Log entry.
    private async Task AssertStillPendingWithoutCancellationAsync(Guid id)
    {
        Assert.Equal("Pending", await GetStatusAsync(DirectorySeed.CarolId, id));
        var entry = Assert.Single(await GetAuditLogAsync(DirectorySeed.CarolId, id));
        Assert.Equal("Created", entry.Event);
    }

    [Fact]
    public async Task Requester_cancels_a_pending_access_request()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await CancelAsync(DirectorySeed.CarolId, id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Cancelled", await GetStatusAsync(DirectorySeed.CarolId, id));
        var auditLog = await GetAuditLogAsync(DirectorySeed.CarolId, id);
        Assert.Equal(["Created", "Cancelled"], auditLog.Select(e => e.Event));
        Assert.Equal(
            new AuditLogEntryDto("Cancelled", DirectorySeed.CarolId, "Pending", "Cancelled", null, null, null),
            auditLog[1]);
    }

    [Theory]
    [InlineData(true)] // Requester is also the Beneficiary
    [InlineData(false)] // Requester is also the System Owner
    public async Task Requester_cancels_even_when_also_beneficiary_or_system_owner(bool requesterIsBeneficiary)
    {
        var id = requesterIsBeneficiary
            ? await CreateAsync(DirectorySeed.CarolId, DirectorySeed.CarolId, DirectorySeed.JiraId)
            : await CreateAsync(DirectorySeed.AliceId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        var requesterId = requesterIsBeneficiary ? DirectorySeed.CarolId : DirectorySeed.AliceId;

        var response = await CancelAsync(requesterId, id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Cancelled", await GetStatusAsync(requesterId, id));
        Assert.Equal(requesterId, (await GetAuditLogAsync(requesterId, id))[1].ActorId);
    }

    public static TheoryData<string> FirstActions => new() { "approve", "reject", "cancel" };

    [Theory]
    [MemberData(nameof(FirstActions))]
    public async Task Cancelling_an_access_request_that_is_not_pending_returns_409(string firstAction)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        var first = firstAction switch
        {
            "approve" => await ApproveAsync(DirectorySeed.AliceId, id),
            "reject" => await RejectAsync(DirectorySeed.AliceId, id),
            _ => await CancelAsync(DirectorySeed.CarolId, id),
        };
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var statusAfterFirst = await GetStatusAsync(DirectorySeed.CarolId, id);

        var response = await CancelAsync(DirectorySeed.CarolId, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(statusAfterFirst, await GetStatusAsync(DirectorySeed.CarolId, id));
        Assert.Equal(2, (await GetAuditLogAsync(DirectorySeed.CarolId, id)).Length);
    }

    public static TheoryData<Guid> BeneficiaryAndSystemOwnerOfCarolsRequestForDaveToJira => new()
    {
        DirectorySeed.DaveId, // Beneficiary
        DirectorySeed.AliceId, // System Owner
    };

    [Theory]
    [MemberData(nameof(BeneficiaryAndSystemOwnerOfCarolsRequestForDaveToJira))]
    public async Task Beneficiary_or_system_owner_who_is_not_the_requester_gets_403(Guid callerId)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await CancelAsync(callerId, id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertStillPendingWithoutCancellationAsync(id);
    }

    [Fact]
    public async Task User_who_does_not_see_the_access_request_gets_404()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await CancelAsync(DirectorySeed.BobId, id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertStillPendingWithoutCancellationAsync(id);
    }

    [Fact]
    public async Task Cancelling_an_unknown_access_request_returns_404()
    {
        var response = await CancelAsync(DirectorySeed.CarolId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_cancel_and_approve_apply_one_and_return_409_for_the_others()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        // Several of each action widen the race window; exactly one may win.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).SelectMany(_ => new[]
            {
                CancelAsync(DirectorySeed.CarolId, id),
                ApproveAsync(DirectorySeed.AliceId, id),
            }));

        Assert.Equal(
            [HttpStatusCode.NoContent, .. Enumerable.Repeat(HttpStatusCode.Conflict, responses.Length - 1)],
            responses.Select(r => r.StatusCode).Order());
        var auditLog = await GetAuditLogAsync(DirectorySeed.CarolId, id);
        Assert.Equal(2, auditLog.Length);
        Assert.Equal(auditLog[1].StatusAfter, await GetStatusAsync(DirectorySeed.CarolId, id));
    }
}
