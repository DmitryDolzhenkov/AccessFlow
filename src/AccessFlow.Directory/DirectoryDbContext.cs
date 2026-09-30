using Microsoft.EntityFrameworkCore;

namespace AccessFlow.Directory;

internal sealed class User
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
}

internal sealed class AccessSystem
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid OwnerId { get; init; }
    public required string ProvisioningUrl { get; init; }
}

internal sealed class DirectoryDbContext(DbContextOptions<DirectoryDbContext> options) : DbContext(options)
{
    public const string Schema = "directory";

    public DbSet<User> Users => Set<User>();
    public DbSet<AccessSystem> Systems => Set<AccessSystem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.Name).HasMaxLength(200);
            user.HasData(DirectorySeed.Users);
        });

        modelBuilder.Entity<AccessSystem>(system =>
        {
            system.ToTable("systems");
            system.Property(s => s.Name).HasMaxLength(200);
            system.Property(s => s.ProvisioningUrl).HasMaxLength(2000);
            system.HasOne<User>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Restrict);
            system.HasData(DirectorySeed.Systems);
        });
    }
}
