namespace AccessFlow.Directory;

/// <summary>
/// Read-only view of Users and Systems for other modules.
/// </summary>
public interface IDirectory
{
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);

    Task<SystemInfo?> FindSystemAsync(Guid systemId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetOwnedSystemIdsAsync(Guid ownerId, CancellationToken cancellationToken);
}

public sealed record SystemInfo(Guid Id, Guid OwnerId, string ProvisioningUrl);
