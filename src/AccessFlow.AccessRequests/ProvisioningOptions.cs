namespace AccessFlow.AccessRequests;

/// <summary>
/// Configuration of Provisioning, bound by the host from the "Provisioning" section (BR-20).
/// </summary>
public sealed class ProvisioningOptions
{
    public const string Section = "Provisioning";

    // How often the background worker polls the outbox.
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    // Attempts in total, including the first one.
    public int MaxAttempts { get; init; } = 5;

    // The delay before the second attempt; each following delay is twice the previous one.
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);

    // How long one attempt waits for the System to answer.
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}
