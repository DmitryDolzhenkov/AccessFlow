namespace AccessFlow.Directory;

/// <summary>
/// Reference data. Records referenced by Access Requests are never removed (PRD A-01).
/// </summary>
internal static class DirectorySeed
{
    public static readonly Guid AliceId = new("0198f3a0-0000-7000-8000-000000000001");
    public static readonly Guid BobId = new("0198f3a0-0000-7000-8000-000000000002");
    public static readonly Guid CarolId = new("0198f3a0-0000-7000-8000-000000000003");
    public static readonly Guid DaveId = new("0198f3a0-0000-7000-8000-000000000004");

    public static readonly Guid JiraId = new("0198f3a0-0000-7000-8000-000000000101");
    public static readonly Guid GitLabId = new("0198f3a0-0000-7000-8000-000000000102");

    public static readonly User[] Users =
    [
        new() { Id = AliceId, Name = "Alice" },
        new() { Id = BobId, Name = "Bob" },
        new() { Id = CarolId, Name = "Carol" },
        new() { Id = DaveId, Name = "Dave" },
    ];

    public static readonly AccessSystem[] Systems =
    [
        new() { Id = JiraId, Name = "Jira", OwnerId = AliceId, ProvisioningUrl = "http://localhost:5199/provisioning/jira" },
        new() { Id = GitLabId, Name = "GitLab", OwnerId = BobId, ProvisioningUrl = "http://localhost:5199/provisioning/gitlab" },
    ];
}
