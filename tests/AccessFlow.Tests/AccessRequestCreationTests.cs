using System.Net;
using System.Net.Http.Json;
using AccessFlow.Directory;

namespace AccessFlow.Tests;

[Collection(ApiCollection.Name)]
public sealed class AccessRequestCreationTests(AccessFlowApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record AccessRequestDto(
        Guid Id, Guid RequesterId, Guid BeneficiaryId, Guid SystemId, string Justification, string Status, DateTimeOffset CreatedAt);

    private sealed record CreatedDto(Guid Id);

    private static object CreateBody(Guid beneficiaryId, Guid systemId, string? justification = "Need it for work") =>
        new { beneficiaryId, systemId, justification };

    private async Task<Guid> CreateAsync(Guid requesterId, Guid beneficiaryId, Guid systemId)
    {
        var response = await factory.CreateClientAs(requesterId)
            .PostAsJsonAsync("/access-requests", CreateBody(beneficiaryId, systemId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
    }

    public static TheoryData<string?> InvalidUserIdHeaders => new()
    {
        null,
        "not-a-guid",
        Guid.NewGuid().ToString(),
    };

    [Theory]
    [MemberData(nameof(InvalidUserIdHeaders))]
    public async Task Create_without_a_known_caller_returns_401(string? userIdHeader)
    {
        var response = await factory.CreateClientWithUserIdHeader(userIdHeader)
            .PostAsJsonAsync("/access-requests", CreateBody(DirectorySeed.CarolId, DirectorySeed.JiraId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidUserIdHeaders))]
    public async Task Get_without_a_known_caller_returns_401(string? userIdHeader)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.CarolId, DirectorySeed.JiraId);

        var response = await factory.CreateClientWithUserIdHeader(userIdHeader).GetAsync($"/access-requests/{id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requester_creates_a_pending_access_request_for_themselves()
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId)
            .PostAsJsonAsync("/access-requests", CreateBody(DirectorySeed.CarolId, DirectorySeed.JiraId, "Sprint planning"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedDto>();
        Assert.Equal($"/access-requests/{created!.Id}", response.Headers.Location?.AbsolutePath);

        var request = await factory.CreateClientAs(DirectorySeed.CarolId)
            .GetFromJsonAsync<AccessRequestDto>($"/access-requests/{created.Id}");

        Assert.NotNull(request);
        Assert.Equal(created.Id, request.Id);
        Assert.Equal(DirectorySeed.CarolId, request.RequesterId);
        Assert.Equal(DirectorySeed.CarolId, request.BeneficiaryId);
        Assert.Equal(DirectorySeed.JiraId, request.SystemId);
        Assert.Equal("Sprint planning", request.Justification);
        Assert.Equal("Pending", request.Status);
    }

    [Fact]
    public async Task Requester_is_the_caller_when_requesting_for_another_user()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.GitLabId);

        var request = await factory.CreateClientAs(DirectorySeed.CarolId)
            .GetFromJsonAsync<AccessRequestDto>($"/access-requests/{id}");

        Assert.Equal(DirectorySeed.CarolId, request!.RequesterId);
        Assert.Equal(DirectorySeed.DaveId, request.BeneficiaryId);
        Assert.Equal("Pending", request.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_without_justification_returns_400(string? justification)
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId)
            .PostAsJsonAsync("/access-requests", CreateBody(DirectorySeed.CarolId, DirectorySeed.JiraId, justification));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Justification", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_for_unknown_beneficiary_returns_400()
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId)
            .PostAsJsonAsync("/access-requests", CreateBody(Guid.NewGuid(), DirectorySeed.JiraId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("BeneficiaryId", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_for_unknown_system_returns_400()
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId)
            .PostAsJsonAsync("/access-requests", CreateBody(DirectorySeed.CarolId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SystemId", await response.Content.ReadAsStringAsync());
    }

    public static TheoryData<Guid> ParticipantsOfCarolsRequestForDaveToJira => new()
    {
        DirectorySeed.CarolId, // Requester
        DirectorySeed.DaveId, // Beneficiary
        DirectorySeed.AliceId, // System Owner of Jira
    };

    [Theory]
    [MemberData(nameof(ParticipantsOfCarolsRequestForDaveToJira))]
    public async Task Access_request_is_visible_to_its_participants(Guid viewerId)
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await factory.CreateClientAs(viewerId).GetAsync($"/access-requests/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Access_request_is_not_found_for_other_users()
    {
        var id = await CreateAsync(DirectorySeed.CarolId, DirectorySeed.DaveId, DirectorySeed.JiraId);

        var response = await factory.CreateClientAs(DirectorySeed.BobId).GetAsync($"/access-requests/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_access_request_is_not_found()
    {
        var response = await factory.CreateClientAs(DirectorySeed.CarolId).GetAsync($"/access-requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
