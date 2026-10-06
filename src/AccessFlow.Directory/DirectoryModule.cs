using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AccessFlow.Directory;

public static class DirectoryModule
{
    public static IServiceCollection AddDirectoryModule(this IServiceCollection services)
    {
        services.AddScoped<IDirectory, EfDirectory>();
        return services;
    }
}

internal sealed class EfDirectory(DbContext db) : IDirectory
{
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().AnyAsync(u => u.Id == userId, cancellationToken);

    public Task<SystemInfo?> FindSystemAsync(Guid systemId, CancellationToken cancellationToken) =>
        db.Set<AccessSystem>()
            .Where(s => s.Id == systemId)
            .Select(s => new SystemInfo(s.Id, s.OwnerId, s.ProvisioningUrl))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetOwnedSystemIdsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Set<AccessSystem>()
            .Where(s => s.OwnerId == ownerId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
}
