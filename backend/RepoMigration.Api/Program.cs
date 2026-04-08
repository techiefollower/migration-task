using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using RepoMigration.Api.Authentication;
using RepoMigration.Infrastructure;
using RepoMigration.Infrastructure.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings.Development.local.json", optional: true, reloadOnChange: true);
}

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

var disableAuth = builder.Environment.IsDevelopment() &&
    builder.Configuration.GetValue("AzureAd:DisableAuthentication", false);

if (disableAuth)
{
    builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
            DevelopmentAuthenticationHandler.SchemeName,
            _ => { });
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder(DevelopmentAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .Build();
    });
}
else
{
    var tenantId = builder.Configuration["AzureAd:TenantId"];
    if (string.IsNullOrWhiteSpace(tenantId))
    {
        throw new InvalidOperationException(
            "AzureAd:TenantId must be set for production. For local dev without Entra ID, set AzureAd:DisableAuthentication to true in appsettings.Development.json.");
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
    });
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? Array.Empty<string>();
var localhost = new[] { "http://localhost:5173", "http://127.0.0.1:5173" };
var allOrigins = corsOrigins
    .Concat(localhost)
    .Where(static o => !string.IsNullOrWhiteSpace(o))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

Log.Information(
    "RepoMigration.Infrastructure assembly: {Name} v{Version}",
    typeof(MigrationJobExecutor).Assembly.GetName().Name,
    typeof(MigrationJobExecutor).Assembly.GetName().Version);
Log.Information("Authentication: {Mode}", disableAuth ? "Development (no JWT)" : "Microsoft Entra ID (JWT)");

{
    var skipInv = builder.Configuration.GetValue("GhAdo2Gh:SkipInventoryReport", true);
    Log.Information("GhAdo2Gh:SkipInventoryReport = {SkipInventory} (inventory-report runs only when this is false)", skipInv);

    var ghResolver = app.Services.GetRequiredService<IGhCliPathResolver>();
    var ghPath = ghResolver.ResolveGhExecutablePath();
    if (ghPath == "gh" && OperatingSystem.IsWindows())
    {
        Log.Warning(
            "GhCli resolves to bare \"gh\" — the API process cannot see GitHub CLI on PATH. Set GhCli:ExecutablePath in appsettings.json (full path from PowerShell: (Get-Command gh).Source) or set env GH_CLI_PATH, then restart the API. Rebuild after editing appsettings so bin\\Debug copies the file.");
    }
    else
        Log.Information("GhCli resolved to: {GhPath}", ghPath);
}

app.UseSerilogRequestLogging();

app.UseCors();

// In Development, Vite proxies to http://localhost:5096. HTTPS redirection breaks that proxy (307 → https + cert issues).
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Json(new { ok = true, service = "RepoMigration.Api" }))
    .AllowAnonymous();

app.MapGet("/api/me", (ClaimsPrincipal user) =>
    {
        if (user.Identity?.IsAuthenticated != true)
            return Results.Unauthorized();
        var name = user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("preferred_username")?.Value
            ?? user.Identity.Name;
        var oid = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("oid")?.Value;
        return Results.Json(new { name, objectId = oid });
    })
    .RequireAuthorization();

app.MapControllers();

app.UseSwagger();
app.UseSwaggerUI();

try
{
    Log.Information("Starting Repo Migration API");
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
