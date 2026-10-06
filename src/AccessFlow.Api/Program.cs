using System.Text.Json.Serialization;
using AccessFlow.AccessRequests;
using AccessFlow.Api.Identity;
using AccessFlow.Api.Persistence;
using AccessFlow.Api.Provisioning;
using AccessFlow.Directory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AccessFlow")
    ?? throw new InvalidOperationException("Connection string 'AccessFlow' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AccessFlowDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AccessFlowDbContext>());
builder.Services.AddDirectoryModule();
builder.Services.AddAccessRequestsModule();

builder.Services.AddOptions<ProvisioningOptions>()
    .Bind(builder.Configuration.GetSection(ProvisioningOptions.Section))
    .Validate(options => options.PollInterval > TimeSpan.Zero, "Provisioning:PollInterval must be positive.")
    .Validate(options => options.MaxAttempts >= 1, "Provisioning:MaxAttempts must be at least 1.")
    .Validate(options => options.BaseDelay >= TimeSpan.Zero, "Provisioning:BaseDelay must not be negative.")
    .Validate(options => options.Timeout > TimeSpan.Zero, "Provisioning:Timeout must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ProvisioningOptions>>().Value);
// BR-20: an attempt the System does not answer within the timeout fails as a timeout.
builder.Services.AddHttpClient<ProvisioningService>((sp, client) => client.Timeout = sp.GetRequiredService<ProvisioningOptions>().Timeout);
builder.Services.AddHostedService<ProvisioningWorker>();

builder.Services
    .AddAuthentication(UserIdHeaderAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, UserIdHeaderAuthenticationHandler>(UserIdHeaderAuthenticationHandler.SchemeName, null);
builder.Services
    .AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AccessFlowDbContext>().Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
