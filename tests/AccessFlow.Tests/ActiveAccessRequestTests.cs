using System.Net;
using System.Net.Http.Json;
using AccessFlow.AccessRequests;
using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AccessFlow.Tests;

/// <summary>
/// BR-08, BR-09: at most one Active Access Request (Pending or Approved) per Beneficiary + System.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ActiveAccessRequestTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> PostAsync(Guid requesterId, Guid beneficiaryId, Guid systemId) =>
        factory.CreateClientAs(requesterId)
            .PostAsJsonAsync("/access-requests", new { beneficiaryId, systemId, justification = "Need it for work" });

    private async Task CreateAsync(Guid requesterId, Guid beneficiaryId, Guid systemId) =>
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(requesterId, beneficiaryId, systemId)).StatusCode);

    // Status transitions arrive in later issues, so tests set the status directly in the database.
    private Task SetStatusOfAllAsync(string status) =>
        factory.ExecuteSqlAsync($"UPDATE access_requests.access_requests SET \"Status\" = {status}");

    private Task InsertDirectlyAsync(Guid beneficiaryId, Guid systemId, string status) =>
        factory.ExecuteSqlAsync($"""
            INSERT INTO access_requests.access_requests
                ("Id", "RequesterId", "BeneficiaryId", "SystemId", "Justification", "Status", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {beneficiaryId}, {beneficiaryId}, {systemId}, 'Direct insert', {status}, now())
            """);

    private Task<int> CountAsync(Guid beneficiaryId, Guid systemId) =>
        factory.UseDbAsync(db => db.Database
            .SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM access_requests.access_requests
                WHERE "BeneficiaryId" = {beneficiaryId} AND "SystemId" = {systemId}
                """)
            .SingleAsync());

    public static TheoryData<string> ActiveStatuses => [nameof(AccessRequestStatus.Pending), nameof(AccessRequestStatus.Approved)];

    public static TheoryData<string> FinalStatuses =>
    [
        nameof(AccessRequestStatus.Provisioned),
        nameof(AccessRequestStatus.ProvisioningFailed),
        nameof(AccessRequestStatus.Rejected),
        nameof(AccessRequestStatus.Cancelled),
    ];

    [Theory]
    [MemberData(nameof(ActiveStatuses))]
    public async Task Second_access_request_for_the_same_pair_while_active_returns_409(string activeStatus)
    {
        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        await SetStatusOfAllAsync(activeStatus);

        var second = await PostAsync(DirectorySeed.BobId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(1, await CountAsync(DirectorySeed.DaveId, DirectorySeed.JiraId));
    }

    [Fact]
    public async Task Concurrent_access_requests_for_the_same_pair_create_one_and_return_one_409()
    {
        var responses = await Task.WhenAll(
            PostAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId),
            PostAsync(DirectorySeed.DaveId, DirectorySeed.DaveId, DirectorySeed.JiraId));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await CountAsync(DirectorySeed.DaveId, DirectorySeed.JiraId));
    }

    [Theory]
    [MemberData(nameof(ActiveStatuses))]
    public async Task Database_rejects_a_second_active_access_request_for_the_same_pair(string activeStatus)
    {
        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var error = await Assert.ThrowsAsync<PostgresException>(
            () => InsertDirectlyAsync(DirectorySeed.DaveId, DirectorySeed.JiraId, activeStatus));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal(AccessRequestConfiguration.SingleActiveIndex, error.ConstraintName);
    }

    [Fact]
    public async Task Access_requests_for_another_beneficiary_or_another_system_are_created()
    {
        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.CarolId, DirectorySeed.JiraId);
        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.GitLabId);
    }

    [Theory]
    [MemberData(nameof(FinalStatuses))]
    public async Task New_access_request_for_the_same_pair_is_created_after_a_final_status(string finalStatus)
    {
        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);
        await SetStatusOfAllAsync(finalStatus);

        await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        Assert.Equal(2, await CountAsync(DirectorySeed.DaveId, DirectorySeed.JiraId));
    }
}
