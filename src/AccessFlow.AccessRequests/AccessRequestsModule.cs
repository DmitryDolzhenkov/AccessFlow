using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace AccessFlow.AccessRequests;

public static class AccessRequestsModule
{
    public static IServiceCollection AddAccessRequestsModule(this IServiceCollection services)
    {
        services.AddScoped<AccessRequestService>();
        return services;
    }
}

internal sealed class AccessRequestConfiguration : IEntityTypeConfiguration<AccessRequest>
{
    // Directory entity types are internal to their module, so cross-module foreign keys refer to them by name.
    public const string UserEntity = "AccessFlow.Directory.User";
    private const string SystemEntity = "AccessFlow.Directory.AccessSystem";

    public const string SingleActiveIndex = "IX_access_requests_active_BeneficiaryId_SystemId";

    public void Configure(EntityTypeBuilder<AccessRequest> request)
    {
        request.ToTable("access_requests", "access_requests");
        // BR-15: a status change saves only if the status is still the one that was read,
        // so of two concurrent actions on one Access Request the second gets a concurrency conflict.
        request.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsConcurrencyToken();

        // BR-08: at most one Active Access Request per Beneficiary + System, enforced by the database.
        request.HasIndex(r => new { r.BeneficiaryId, r.SystemId })
            .IsUnique()
            .HasFilter($"\"Status\" IN ('{AccessRequestStatus.Pending}', '{AccessRequestStatus.Approved}')")
            .HasDatabaseName(SingleActiveIndex);
        // The filtered index above does not cover all rows, so the Beneficiary foreign key keeps its own index.
        request.HasIndex(r => r.BeneficiaryId);

        request.HasOne(UserEntity, navigationName: null).WithMany().HasForeignKey(nameof(AccessRequest.RequesterId)).OnDelete(DeleteBehavior.Restrict);
        request.HasOne(UserEntity, navigationName: null).WithMany().HasForeignKey(nameof(AccessRequest.BeneficiaryId)).OnDelete(DeleteBehavior.Restrict);
        request.HasOne(SystemEntity, navigationName: null).WithMany().HasForeignKey(nameof(AccessRequest.SystemId)).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> entry)
    {
        entry.ToTable("audit_log", "access_requests");
        entry.Property(e => e.Sequence).ValueGeneratedOnAdd();
        entry.Property(e => e.Event).HasConversion<string>().HasMaxLength(32);
        entry.Property(e => e.StatusBefore).HasConversion<string>().HasMaxLength(32);
        entry.Property(e => e.StatusAfter).HasConversion<string>().HasMaxLength(32);

        entry.HasOne<AccessRequest>().WithMany().HasForeignKey(e => e.AccessRequestId).OnDelete(DeleteBehavior.Restrict);
        entry.HasOne(AccessRequestConfiguration.UserEntity, navigationName: null).WithMany().HasForeignKey(nameof(AuditLogEntry.ActorId)).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProvisioningOutboxEntryConfiguration : IEntityTypeConfiguration<ProvisioningOutboxEntry>
{
    public void Configure(EntityTypeBuilder<ProvisioningOutboxEntry> entry)
    {
        entry.ToTable("provisioning_outbox", "access_requests");
        // The worker polls only unprocessed entries that are due.
        entry.HasIndex(e => e.NextAttemptAt).HasFilter("\"ProcessedAt\" IS NULL");

        // Not unique: a losing concurrent approval must fail on the status concurrency token (BR-15), not on this index.
        entry.HasOne<AccessRequest>().WithMany().HasForeignKey(e => e.AccessRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}
