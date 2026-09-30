using Microsoft.EntityFrameworkCore;

namespace AccessFlow.AccessRequests;

internal sealed class AccessRequestsDbContext(DbContextOptions<AccessRequestsDbContext> options) : DbContext(options)
{
    public const string Schema = "access_requests";

    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AccessRequest>(request =>
        {
            request.ToTable("access_requests");
            request.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
        });
    }
}
