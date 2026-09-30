using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccessFlow.AccessRequests;

public static class AccessRequestsModule
{
    public static IServiceCollection AddAccessRequestsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccessRequestsDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AccessRequestsDbContext.Schema)));
        services.AddScoped(sp => new AccessRequestService(
            sp.GetRequiredService<AccessRequestsDbContext>(),
            sp.GetRequiredService<IDirectory>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    public static async Task MigrateAccessRequestsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccessRequestsDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
