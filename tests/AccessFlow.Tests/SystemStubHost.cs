using AccessFlow.SystemStub;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AccessFlow.Tests;

/// <summary>
/// Runs the System stub in memory; AccessFlow reaches it through <see cref="CreateHandler"/>.
/// </summary>
public sealed class SystemStubHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private SystemStubHost(WebApplication app) => _app = app;

    public static async Task<SystemStubHost> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSystemStub();

        var app = builder.Build();
        app.MapSystemStub();
        await app.StartAsync();
        return new SystemStubHost(app);
    }

    public IReadOnlyList<ReceivedProvisioningCall> ReceivedCalls =>
        _app.Services.GetRequiredService<ReceivedProvisioningCalls>().ToList();

    public HttpMessageHandler CreateHandler() => _app.GetTestServer().CreateHandler();

    public HttpClient CreateClient() => _app.GetTestClient();

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
