using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccessFlow.Directory;

public static class DirectoryModule
{
    public static IServiceCollection AddDirectoryModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DirectoryDbContext>(options => options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DirectoryDbContext.Schema)));
        services.AddScoped<IDirectory, EfDirectory>();
        return services;
    }

    public static async Task MigrateDirectoryAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DirectoryDbContext>().Database.MigrateAsync(cancellationToken);
    }
}

internal sealed class EfDirectory(DirectoryDbContext db) : IDirectory
{
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId, cancellationToken);

    public Task<SystemInfo?> FindSystemAsync(Guid systemId, CancellationToken cancellationToken) =>
        db.Systems
            .Where(s => s.Id == systemId)
            .Select(s => new SystemInfo(s.Id, s.OwnerId))
            .SingleOrDefaultAsync(cancellationToken);
}
