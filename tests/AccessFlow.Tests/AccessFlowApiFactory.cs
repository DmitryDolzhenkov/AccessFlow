using AccessFlow.AccessRequests;
using AccessFlow.Api.Identity;
using AccessFlow.Api.Persistence;
using AccessFlow.Api.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

namespace AccessFlow.Tests;

/// <summary>
/// Runs the API against a real PostgreSQL in a container; migrations are applied on startup.
/// Systems are replaced by the in-memory System stub.
/// </summary>
public sealed class AccessFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public SystemStubHost System { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        System = await SystemStubHost.StartAsync();
        await _postgres.StartAsync();
        _ = Server;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await System.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:AccessFlow", _postgres.GetConnectionString());
        builder.UseSetting("Provisioning:PollInterval", "00:00:00.050");
        builder.ConfigureTestServices(services =>
        {
            // Only instances from StartWithProvisioning provision, so other tests see Approved stay Approved.
            services.Remove(services.Single(d => d.ImplementationType == typeof(ProvisioningWorker)));
            services.AddHttpClient<ProvisioningService>().ConfigurePrimaryHttpMessageHandler(System.CreateHandler);
        });
    }

    /// <summary>
    /// Starts another instance of the app on the same database with the Provisioning worker running
    /// and the System reached through <paramref name="system"/>. Disposing it stops the instance, as a shutdown would.
    /// </summary>
    public WebApplicationFactory<Program> StartWithProvisioning(Func<HttpMessageHandler>? system = null, ILoggerProvider? logs = null)
    {
        var app = WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHostedService<ProvisioningWorker>();
                if (system is not null)
                    services.AddHttpClient<ProvisioningService>().ConfigurePrimaryHttpMessageHandler(system);
            });
            if (logs is not null)
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });
        _ = app.Server;
        return app;
    }

    public HttpClient CreateClientAs(Guid userId) => CreateClientWithUserIdHeader(userId.ToString());

    public HttpClient CreateClientWithUserIdHeader(string? headerValue)
    {
        var client = CreateClient();
        if (headerValue is not null)
            client.DefaultRequestHeaders.Add(UserIdHeaderAuthenticationHandler.HeaderName, headerValue);
        return client;
    }

    /// <summary>
    /// Removes all Access Requests, keeping the seeded Directory. Tests in the collection run sequentially.
    /// </summary>
    public Task ResetAsync() => ExecuteSqlAsync($"TRUNCATE access_requests.access_requests CASCADE");

    public Task<int> ExecuteSqlAsync(FormattableString sql) => UseDbAsync(db => db.Database.ExecuteSqlAsync(sql));

    public async Task<T> UseDbAsync<T>(Func<AccessFlowDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AccessFlowDbContext>());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<AccessFlowApiFactory>
{
    public const string Name = "api";
}
