using AccessFlow.Api.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace AccessFlow.Tests;

/// <summary>
/// Runs the API against a real PostgreSQL in a container; migrations are applied on startup.
/// </summary>
public sealed class AccessFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Server;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:AccessFlow", _postgres.GetConnectionString());
    }

    public HttpClient CreateClientAs(Guid userId) => CreateClientWithUserIdHeader(userId.ToString());

    public HttpClient CreateClientWithUserIdHeader(string? headerValue)
    {
        var client = CreateClient();
        if (headerValue is not null)
            client.DefaultRequestHeaders.Add(UserIdHeaderAuthenticationHandler.HeaderName, headerValue);
        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<AccessFlowApiFactory>
{
    public const string Name = "api";
}
