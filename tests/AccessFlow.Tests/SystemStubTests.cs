using System.Net;
using System.Net.Http.Json;
using System.Text;
using AccessFlow.SystemStub;

namespace AccessFlow.Tests;

/// <summary>
/// BR-17: the System stub implements the Provisioning contract.
/// </summary>
public sealed class SystemStubTests : IAsyncLifetime
{
    private SystemStubHost _stub = null!;

    public async Task InitializeAsync() => _stub = await SystemStubHost.StartAsync();

    public async Task DisposeAsync() => await _stub.DisposeAsync();

    private Task<HttpResponseMessage> PostAsync(string json) =>
        _stub.CreateClient().PostAsync("/provisioning/jira", new StringContent(json, Encoding.UTF8, "application/json"));

    [Fact]
    public async Task Grants_access_on_a_contract_call()
    {
        var call = new ProvisioningCall(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var response = await _stub.CreateClient().PostAsJsonAsync("/provisioning/jira", call);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([new ReceivedProvisioningCall("jira", call)], _stub.ReceivedCalls);
    }

    [Fact]
    public async Task Repeated_call_with_the_same_access_request_id_also_succeeds()
    {
        var call = new ProvisioningCall(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var first = await _stub.CreateClient().PostAsJsonAsync("/provisioning/jira", call);
        var repeated = await _stub.CreateClient().PostAsJsonAsync("/provisioning/jira", call);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(2, _stub.ReceivedCalls.Count);
    }

    public static TheoryData<string> NonContractBodies => new()
    {
        """{ "beneficiaryId": "0198f3a0-0000-7000-8000-000000000004", "systemId": "0198f3a0-0000-7000-8000-000000000101" }""",
        """{ "AccessRequestId": "0198f3a0-0000-7000-8000-0000000000aa", "BeneficiaryId": "0198f3a0-0000-7000-8000-000000000004", "SystemId": "0198f3a0-0000-7000-8000-000000000101" }""",
        "{}",
    };

    [Theory]
    [MemberData(nameof(NonContractBodies))]
    public async Task Call_that_breaks_the_contract_returns_400_and_grants_nothing(string json)
    {
        var response = await PostAsync(json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_stub.ReceivedCalls);
    }
}
