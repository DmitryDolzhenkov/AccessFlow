using System.Text.Json.Serialization;
using AccessFlow.AccessRequests;
using AccessFlow.Api.Identity;
using AccessFlow.Directory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("AccessFlow")
    ?? throw new InvalidOperationException("Connection string 'AccessFlow' is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDirectoryModule(connectionString);
builder.Services.AddAccessRequestsModule(connectionString);

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

await app.Services.MigrateDirectoryAsync();
await app.Services.MigrateAccessRequestsAsync();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
