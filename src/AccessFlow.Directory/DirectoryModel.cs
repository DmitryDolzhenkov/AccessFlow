using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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

internal static class DirectorySchema
{
    public const string Name = "directory";
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> user)
    {
        user.ToTable("users", DirectorySchema.Name);
        user.Property(u => u.Name).HasMaxLength(200);
        user.HasData(DirectorySeed.Users);
    }
}

internal sealed class AccessSystemConfiguration : IEntityTypeConfiguration<AccessSystem>
{
    public void Configure(EntityTypeBuilder<AccessSystem> system)
    {
        system.ToTable("systems", DirectorySchema.Name);
        system.Property(s => s.Name).HasMaxLength(200);
        system.Property(s => s.ProvisioningUrl).HasMaxLength(2000);
        system.HasOne<User>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Restrict);
        system.HasData(DirectorySeed.Systems);
    }
}
