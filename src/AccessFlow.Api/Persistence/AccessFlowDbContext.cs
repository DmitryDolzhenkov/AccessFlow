using AccessFlow.AccessRequests;
using AccessFlow.Directory;
using Microsoft.EntityFrameworkCore;

namespace AccessFlow.Api.Persistence;

/// <summary>
/// The single DbContext of the modular monolith. Each module contributes its entity configurations
/// and works with the context through the <see cref="DbContext"/> base type.
/// </summary>
public sealed class AccessFlowDbContext(DbContextOptions<AccessFlowDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Directory goes first: other modules reference its entity types.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DirectoryModule).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccessRequestsModule).Assembly);
    }
}
