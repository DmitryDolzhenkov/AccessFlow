using System.Net;
using System.Net.Http.Json;
using AccessFlow.Directory;

namespace AccessFlow.Tests;

/// <summary>
/// BR-23…BR-29: the Audit Log of an Access Request.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditLogTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record AuditLogEntryDto(
        Guid AccessRequestId,
        string Event,
        Guid ActorId,
        DateTimeOffset OccurredAt,
        string? StatusBefore,
        string StatusAfter,
        string? Justification);

    private sealed record AccessRequestDto(Guid Id, DateTimeOffset CreatedAt);

    private sealed record CreatedDto(Guid Id);

    private Task<HttpResponseMessage> PostAsync(Guid requesterId, Guid beneficiaryId, Guid systemId, string justification = "Need it for work") =>
        factory.CreateClientAs(requesterId)
            .PostAsJsonAsync("/access-requests", new { beneficiaryId, systemId, justification });

    private async Task<Guid> CreateAsync(Guid requesterId, Guid beneficiaryId, Guid systemId, string justification = "Need it for work")
    {
        var response = await PostAsync(requesterId, beneficiaryId, systemId, justification);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
    }

    private async Task<AuditLogEntryDto[]> GetAuditLogAsync(Guid viewerId, Guid id) =>
        (await factory.CreateClientAs(viewerId).GetFromJsonAsync<AuditLogEntryDto[]>($"/access-requests/{id}/audit-log"))!;

    [Fact]
    public async Task Creating_an_access_request_records_a_created_entry()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId, "Sprint planning");
        var request = await factory.CreateClientAs(DirectorySeed.CarolId).GetFromJsonAsync<AccessRequestDto>($"/access-requests/{id}");

        var entry = Assert.Single(await GetAuditLogAsync(DirectorySeed.CarolId, id));

        Assert.Equal(id, entry.AccessRequestId);
        Assert.Equal("Created", entry.Event);
        Assert.Equal(DirectorySeed.CarolId, entry.ActorId);
        Assert.Equal(request!.CreatedAt, entry.OccurredAt);
        Assert.Null(entry.StatusBefore);
        Assert.Equal("Pending", entry.StatusAfter);
        Assert.Equal("Sprint planning", entry.Justification);
    }

    [Fact]
    public async Task Rejected_duplicate_access_request_records_nothing_in_the_audit_log_of_the_active_one()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var duplicate = await PostAsync(DirectorySeed.BobId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var entry = Assert.Single(await GetAuditLogAsync(DirectorySeed.CarolId, id));
        Assert.Equal(DirectorySeed.CarolId, entry.ActorId);
    }

    public static TheoryData<Guid> ParticipantsOfCarolsRequestForDaveToJira => new()
    {
        DirectorySeed.CarolId, // Requester
        DirectorySeed.DaveId, // Beneficiary
        DirectorySeed.AliceId, // System Owner of Jira
    };

    [Theory]
    [MemberData(nameof(ParticipantsOfCarolsRequestForDaveToJira))]
    public async Task Audit_log_is_visible_to_the_participants_of_the_access_request(Guid viewerId)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var entry = Assert.Single(await GetAuditLogAsync(viewerId, id));

        Assert.Equal(id, entry.AccessRequestId);
    }

    [Fact]
    public async Task Audit_log_is_not_found_for_other_users()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await factory.CreateClientAs(DirectorySeed.BobId).GetAsync($"/access-requests/{id}/audit-log");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Audit_log_of_an_unknown_access_request_is_not_found()
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId).GetAsync($"/access-requests/{Guid.NewGuid()}/audit-log");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public async Task Audit_log_without_a_known_caller_returns_401(string? userIdHeader)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await factory.CreateClientWithUserIdHeader(userIdHeader).GetAsync($"/access-requests/{id}/audit-log");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
