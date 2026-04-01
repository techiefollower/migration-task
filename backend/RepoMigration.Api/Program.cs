using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepoMigration.Infrastructure;
using RepoMigration.Infrastructure.Data;
using RepoMigration.Infrastructure.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

Log.Information(
    "RepoMigration.Infrastructure assembly: {Name} v{Version}",
    typeof(MigrationJobExecutor).Assembly.GetName().Name,
    typeof(MigrationJobExecutor).Assembly.GetName().Version);

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

app.UseSerilogRequestLogging();

app.UseCors();

app.UseHttpsRedirection();

app.UseAuthorization();

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
