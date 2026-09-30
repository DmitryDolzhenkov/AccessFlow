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
    private const string UserEntity = "AccessFlow.Directory.User";
    private const string SystemEntity = "AccessFlow.Directory.AccessSystem";

    public void Configure(EntityTypeBuilder<AccessRequest> request)
    {
        request.ToTable("access_requests", "access_requests");
        request.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);

        request.HasOne(UserEntity, navigationName: null).WithMany().HasForeignKey(nameof(AccessRequest.RequesterId)).OnDelete(DeleteBehavior.Restrict);
        request.HasOne(UserEntity, navigationName: null).WithMany().HasForeignKey(nameof(AccessRequest.BeneficiaryId)).OnDelete(DeleteBehavior.Restrict);
        request.HasOne(SystemEntity, navigationName: null).WithMany().HasForeignKey(nameof(AccessRequest.SystemId)).OnDelete(DeleteBehavior.Restrict);
    }
}
