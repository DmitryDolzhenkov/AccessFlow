using System.Net;
using System.Net.Http.Json;
using AccessFlow.Directory;

namespace AccessFlow.Tests;

/// <summary>
/// Lists "mine" and "pending my decision" (PRD section 5).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccessRequestListTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    // Restores the seeded System Owner that a test may have changed.
    public Task DisposeAsync() => SetSystemOwnerAsync(DirectorySeed.JiraId, DirectorySeed.AliceId);

    private sealed record AccessRequestDto(Guid Id, string Status);

    private sealed record CreatedDto(Guid Id);

    private Task SetSystemOwnerAsync(Guid systemId, Guid ownerId) =>
        factory.ExecuteSqlAsync($"UPDATE directory.systems SET \"OwnerId\" = {ownerId} WHERE \"Id\" = {systemId}");

    private async Task<Guid> CreateAsync(Guid requesterId, Guid beneficiaryId, Guid systemId)
    {
        var response = await factory.CreateClientAs(requesterId)
            .PostAsJsonAsync("/access-requests", new { beneficiaryId, systemId, justification = "Need it for work" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
    }

    private async Task ActAsync(Guid callerId, Guid id, string action)
    {
        var response = await factory.CreateClientAs(callerId)
            .PostAsJsonAsync($"/access-requests/{id}/{action}", new { rejectionReason = "Not needed" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<AccessRequestDto[]> GetMineAsync(Guid callerId) =>
        (await factory.CreateClientAs(callerId).GetFromJsonAsync<AccessRequestDto[]>("/access-requests/mine"))!;

    private async Task<AccessRequestDto[]> GetPendingMyDecisionAsync(Guid callerId) =>
        (await factory.CreateClientAs(callerId).GetFromJsonAsync<AccessRequestDto[]>("/access-requests/pending-my-decision"))!;

    [Fact]
    public async Task Mine_contains_requests_where_caller_is_requester_or_beneficiary_once_newest_first()
    {
        var carolForDave = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        var daveForCarol = await CreateAsync(DirectorySeed.DaveId, DirectorySeed.CarolId, DirectorySeed.GitLabId);
        await CreateAsync(DirectorySeed.DaveId, DirectorySeed.DaveId, DirectorySeed.GitLabId);
        var carolForHerself = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.CarolId, DirectorySeed.JiraId);
        await ActAsync(DirectorySeed.CarolId, carolForDave, "cancel");

        var mine = await GetMineAsync(DirectorySeed.CarolId);

        Assert.Equal([carolForHerself, daveForCarol, carolForDave], mine.Select(r => r.Id));
        Assert.Equal(["Pending", "Pending", "Cancelled"], mine.Select(r => r.Status));
    }

    [Fact]
    public async Task Mine_does_not_contain_requests_where_caller_is_only_the_system_owner()
    {
        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        Assert.Empty(await GetMineAsync(DirectorySeed.AliceId));
        Assert.Empty(await GetMineAsync(DirectorySeed.BobId));
    }

    [Fact]
    public async Task Pending_my_decision_contains_only_pending_requests_to_systems_the_caller_owns_newest_first()
    {
        var rejected = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.CarolId, DirectorySeed.JiraId);
        await ActAsync(DirectorySeed.AliceId, rejected, "reject");
        var approved = await CreateAsync(DirectorySeed.BobId, DirectorySeed.BobId, DirectorySeed.JiraId);
        await ActAsync(DirectorySeed.AliceId, approved, "approve");
        var cancelled = await CreateAsync(DirectorySeed.DaveId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        await ActAsync(DirectorySeed.DaveId, cancelled, "cancel");
        var carolForDave = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        var toGitLab = await CreateAsync(DirectorySeed.DaveId, DirectorySeed.CarolId, DirectorySeed.GitLabId);
        var aliceForHerself = await CreateAsync(DirectorySeed.AliceId, DirectorySeed.AliceId, DirectorySeed.JiraId);

        var pending = await GetPendingMyDecisionAsync(DirectorySeed.AliceId);

        Assert.Equal([aliceForHerself, carolForDave], pending.Select(r => r.Id));
        Assert.All(pending, r => Assert.Equal("Pending", r.Status));
        Assert.Equal([toGitLab], (await GetPendingMyDecisionAsync(DirectorySeed.BobId)).Select(r => r.Id));
    }

    [Fact]
    public async Task Pending_my_decision_does_not_contain_requests_where_caller_is_only_requester_or_beneficiary()
    {
        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        Assert.Empty(await GetPendingMyDecisionAsync(DirectorySeed.CarolId));
        Assert.Empty(await GetPendingMyDecisionAsync(DirectorySeed.DaveId));
    }

    [Fact]
    public async Task Pending_my_decision_follows_the_current_system_owner()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        await SetSystemOwnerAsync(DirectorySeed.JiraId, DirectorySeed.BobId);

        Assert.Empty(await GetPendingMyDecisionAsync(DirectorySeed.AliceId));
        Assert.Equal([id], (await GetPendingMyDecisionAsync(DirectorySeed.BobId)).Select(r => r.Id));
    }

    [Theory]
    [InlineData("/access-requests/mine")]
    [InlineData("/access-requests/pending-my-decision")]
    public async Task Lists_require_a_known_user(string url)
    {
        var response = await factory.CreateClientWithUserIdHeader(null).GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
