using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace AccessFlow.SystemStub;

/// <summary>
/// The body of the Provisioning call that every System accepts (BR-17).
/// </summary>
public sealed record ProvisioningCall(
    [property: JsonRequired] Guid AccessRequestId,
    [property: JsonRequired] Guid BeneficiaryId,
    [property: JsonRequired] Guid SystemId);

public sealed record ReceivedProvisioningCall(string System, ProvisioningCall Call);

/// <summary>
/// The Provisioning calls the stub has received, in arrival order.
/// </summary>
public sealed class ReceivedProvisioningCalls
{
    private readonly ConcurrentQueue<ReceivedProvisioningCall> _calls = new();

    public void Add(ReceivedProvisioningCall call) => _calls.Enqueue(call);

    public IReadOnlyList<ReceivedProvisioningCall> ToList() => [.. _calls];
}

/// <summary>
/// Stands in for every System in development and tests (PRD section 6): implements the BR-17 contract
/// at <c>POST /provisioning/{system}</c> and grants access on every valid call.
/// </summary>
public static class SystemStubEndpoints
{
    public static IServiceCollection AddSystemStub(this IServiceCollection services)
    {
        services.AddSingleton<ReceivedProvisioningCalls>();
        // The contract names the fields exactly, so a differently cased field counts as missing: 400.
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNameCaseInsensitive = false);
        return services;
    }

    public static IEndpointRouteBuilder MapSystemStub(this IEndpointRouteBuilder endpoints)
    {
        // accessRequestId is the idempotency key: a repeated call grants nothing new and also succeeds (BR-17).
        endpoints.MapPost("/provisioning/{system}", (string system, ProvisioningCall call, ReceivedProvisioningCalls received, ILoggerFactory loggers) =>
        {
            received.Add(new ReceivedProvisioningCall(system, call));
            loggers.CreateLogger(typeof(SystemStubEndpoints)).LogInformation(
                "System {System} granted access to Beneficiary {BeneficiaryId} for Access Request {AccessRequestId}.",
                system, call.BeneficiaryId, call.AccessRequestId);

            return Results.Ok();
        });
        return endpoints;
    }
}
