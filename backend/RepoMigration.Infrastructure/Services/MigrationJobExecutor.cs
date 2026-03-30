using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Enums;
using RepoMigration.Infrastructure.Data;

namespace RepoMigration.Infrastructure.Services;

public class MigrationJobExecutor
{
    private static readonly string[] AllowedVisibilities = ["private", "public", "internal"];

    private const string GhNotFoundUserHint =
        "[rm-api-gh] GitHub CLI could not be started. Install: winget install GitHub.cli — then set RepoMigration.Api/appsettings.json GhCli:ExecutablePath to the output of PowerShell (Get-Command gh).Source (double backslashes in JSON), or set GH_CLI_PATH, and restart the API. If you do NOT see [rm-api-gh] at the start of this message, stop the API, delete bin/obj folders, rebuild RepoMigration.Api, and run again.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGhCliPathResolver _ghCliPath;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MigrationJobExecutor> _logger;

    public MigrationJobExecutor(
        IServiceScopeFactory scopeFactory,
        IGhCliPathResolver ghCliPath,
        IConfiguration configuration,
        ILogger<MigrationJobExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _ghCliPath = ghCliPath;
        _configuration = configuration;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("migrations")]
    public async Task RunAsync(Guid migrationId, string adoPat, string githubPat, string githubOwner)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var github = scope.ServiceProvider.GetRequiredService<IGitHubService>();

        var entity = await db.RepoMigrations.FirstOrDefaultAsync(m => m.Id == migrationId).ConfigureAwait(false);
        if (entity == null)
        {
            _logger.LogWarning("Migration job skipped: record {MigrationId} not found", migrationId);
            return;
        }

        if (entity.Status != MigrationStatus.Pending)
        {
            _logger.LogInformation("Migration job skipped: record {MigrationId} is not pending", migrationId);
            return;
        }

        entity.Status = MigrationStatus.InProgress;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync().ConfigureAwait(false);

        var workRoot = Path.Combine(Path.GetTempPath(), "repo-migration", "ado2gh", migrationId.ToString("N"));
        try
        {
            await AppendLogAsync(db, migrationId, "Validating GitHub token…").ConfigureAwait(false);
            var validation = await github.ValidateTokenAsync(githubPat).ConfigureAwait(false);
            if (!validation.Valid || string.IsNullOrWhiteSpace(validation.Login))
            {
                await FailAsync(db, migrationId, validation.Error ?? "GitHub token invalid.").ConfigureAwait(false);
                return;
            }

            if (!AzureDevOpsGitUrlParser.TryParse(entity.SourceUrl, out var adoOrg, out var adoProject, out var adoRepo))
            {
                await FailAsync(
                        db,
                        migrationId,
                        "Could not parse Azure DevOps Git URL for gh ado2gh. Expected https://dev.azure.com/{org}/{project}/_git/{repo} or https://{org}.visualstudio.com/…")
                    .ConfigureAwait(false);
                return;
            }

            var visibility = AllowedVisibilities.Contains(entity.TargetRepoVisibility?.Trim().ToLowerInvariant())
                ? entity.TargetRepoVisibility!.Trim().ToLowerInvariant()
                : "private";

            Directory.CreateDirectory(workRoot);

            var ghExe = _ghCliPath.ResolveGhExecutablePath();
            if (ghExe != "gh" && !File.Exists(ghExe))
            {
                await FailAsync(
                        db,
                        migrationId,
                        $"GitHub CLI not found at GhCli:ExecutablePath '{ghExe}'. Fix appsettings or install GitHub CLI.")
                    .ConfigureAwait(false);
                return;
            }

            await AppendLogAsync(db, migrationId, $"Using GitHub CLI: {ghExe}").ConfigureAwait(false);

            var skipInventory = _configuration.GetValue("GhAdo2Gh:SkipInventoryReport", true);
            await AppendLogAsync(db, migrationId,
                    $"GhAdo2Gh:SkipInventoryReport resolved to {skipInventory} (set false only to run inventory; env override: GhAdo2Gh__SkipInventoryReport).")
                .ConfigureAwait(false);
            if (skipInventory)
            {
                await AppendLogAsync(db, migrationId,
                        "Step 1/3: Skipped gh ado2gh inventory-report (GhAdo2Gh:SkipInventoryReport is true). " +
                        "migrate-repo does not use the CSV output; enable inventory only if you need preflight pipeline discovery (ADO PAT must include Build: Read).")
                    .ConfigureAwait(false);
            }
            else
            {
                await AppendLogAsync(db, migrationId, $"Step 1/3: gh ado2gh inventory-report for org '{adoOrg}'…").ConfigureAwait(false);
                var inventoryArgs = new List<string> { "ado2gh", "inventory-report", "--ado-org", adoOrg };
                var (invCode, invOut) = await GhCliRunner.RunAsync(ghExe, workRoot, inventoryArgs, adoPat, githubPat).ConfigureAwait(false);
                await AppendLogAsync(db, migrationId, SanitizeGhOutput("inventory-report finished.", invOut)).ConfigureAwait(false);
                if (invCode != 0)
                {
                    await FailAsync(db, migrationId,
                            $"gh ado2gh inventory-report failed (exit {invCode}). " +
                            "This often fails at \"Finding Pipelines\" when the ADO PAT lacks Build (Read) or Azure returns a pipeline API error. " +
                            "Set GhAdo2Gh:SkipInventoryReport to true in appsettings.json to run migrate-repo without inventory (recommended for this app). " +
                            $"Or grant Build: Read on the PAT and retry.{Ado2GhExtensionInstallHint(invOut)}")
                        .ConfigureAwait(false);
                    return;
                }
            }

            await AppendLogAsync(db, migrationId,
                    $"Step 2/3: gh ado2gh migrate-repo — ADO {adoOrg}/{adoProject}/{adoRepo} → GitHub {githubOwner}/{entity.RepoName} ({visibility})…")
                .ConfigureAwait(false);
            var migrateArgs = new List<string>
            {
                "ado2gh", "migrate-repo",
                "--ado-org", adoOrg,
                "--ado-team-project", adoProject,
                "--ado-repo", adoRepo,
                "--github-org", githubOwner.Trim(),
                "--github-repo", entity.RepoName,
                "--target-repo-visibility", visibility,
            };
            var (migCode, migOut) = await GhCliRunner.RunAsync(ghExe, workRoot, migrateArgs, adoPat, githubPat).ConfigureAwait(false);
            await AppendLogAsync(db, migrationId, SanitizeGhOutput("migrate-repo output:", migOut)).ConfigureAwait(false);

            var migrateOk = migCode == 0 && LooksLikeMigrateSucceeded(migOut);
            if (!migrateOk)
            {
                await FailAsync(db, migrationId,
                        $"gh ado2gh migrate-repo failed (exit {migCode}). Check logs above; ensure 'gh' and extension 'github/gh-ado2gh' are installed on the server.{Ado2GhExtensionInstallHint(migOut)}")
                    .ConfigureAwait(false);
                return;
            }

            var pipeline = entity.AdoPipeline?.Trim();
            var serviceConn = entity.ServiceConnectionId?.Trim();
            if (!string.IsNullOrEmpty(pipeline) && !string.IsNullOrEmpty(serviceConn))
            {
                await AppendLogAsync(db, migrationId, "Step 3/3: gh ado2gh rewire-pipeline…").ConfigureAwait(false);
                var rewireArgs = new List<string>
                {
                    "ado2gh", "rewire-pipeline",
                    "--ado-org", adoOrg,
                    "--ado-team-project", adoProject,
                    "--ado-pipeline", pipeline,
                    "--github-org", githubOwner.Trim(),
                    "--github-repo", entity.RepoName,
                    "--service-connection-id", serviceConn,
                };
                var (rwCode, rwOut) = await GhCliRunner.RunAsync(ghExe, workRoot, rewireArgs, adoPat, githubPat).ConfigureAwait(false);
                await AppendLogAsync(db, migrationId, SanitizeGhOutput("rewire-pipeline output:", rwOut)).ConfigureAwait(false);
                if (rwCode != 0)
                {
                    await FailAsync(db, migrationId,
                            $"gh ado2gh rewire-pipeline failed (exit {rwCode}). Repository may already be on GitHub; fix pipeline settings and retry.{Ado2GhExtensionInstallHint(rwOut)}")
                        .ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                await AppendLogAsync(db, migrationId, "Step 3/3: Skipped (no ADO pipeline + service connection id configured).").ConfigureAwait(false);
            }

            await AppendLogAsync(db, migrationId, "Migration completed successfully.").ConfigureAwait(false);
            var done = await db.RepoMigrations.FirstAsync(m => m.Id == migrationId).ConfigureAwait(false);
            done.Status = MigrationStatus.Completed;
            done.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2 && OperatingSystem.IsWindows())
        {
            _logger.LogError(ex, "GitHub CLI not found for migration {MigrationId}", migrationId);
            await FailAsync(db, migrationId, GhNotFoundUserHint).ConfigureAwait(false);
        }
        catch (IOException ex) when (ex.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "GitHub CLI not found (IO) for migration {MigrationId}", migrationId);
            await FailAsync(db, migrationId, GhNotFoundUserHint).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsWindowsProcessStartFailureForGh(ex))
        {
            _logger.LogError(ex, "GitHub CLI process start failed for migration {MigrationId}", migrationId);
            await FailAsync(db, migrationId, GhNotFoundUserHint).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration job failed for {MigrationId}", migrationId);
            var detail = SanitizeErrorMessage(ex);
            await FailAsync(db, migrationId, $"Unexpected error: {detail}").ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(workRoot))
                    Directory.Delete(workRoot, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete temp ado2gh directory for {MigrationId}", migrationId);
            }
        }
    }

    private static bool IsWindowsProcessStartFailureForGh(Exception ex)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is Win32Exception w && w.NativeErrorCode == 2)
                return true;
            var m = e.Message ?? string.Empty;
            if (m.Contains("trying to start process", StringComparison.OrdinalIgnoreCase) &&
                m.Contains("gh", StringComparison.OrdinalIgnoreCase))
                return true;
            if (m.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase) &&
                m.Contains("gh", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool LooksLikeMigrateSucceeded(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return true;
        if (output.Contains("State: SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            return true;
        if (output.Contains("State: FAILED", StringComparison.OrdinalIgnoreCase))
            return false;
        if (output.Contains("No operation will be performed", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string Ado2GhExtensionInstallHint(string combinedOutput)
    {
        if (string.IsNullOrEmpty(combinedOutput))
            return string.Empty;
        if (!combinedOutput.Contains("ado2gh", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (!combinedOutput.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return " Install the ado2gh extension on the machine that runs this API (same user as the service if applicable), then restart the API: gh extension install github/gh-ado2gh";
    }

    private static string SanitizeGhOutput(string prefix, string output)
    {
        var cleaned = RedactSecrets(output);
        var tail = cleaned.Length > 4000 ? cleaned[^4000..] : cleaned;
        return string.IsNullOrWhiteSpace(tail) ? prefix : $"{prefix} {tail}";
    }

    private static string RedactSecrets(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var noPat = Regex.Replace(text, @"https?://[^:]+:[^@]+@", "https://***:***@", RegexOptions.IgnoreCase);
        return noPat;
    }

    private static string SanitizeErrorMessage(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e != null && parts.Count < 4; e = e.InnerException!)
        {
            var m = e.Message?.Trim();
            if (!string.IsNullOrEmpty(m))
                parts.Add(RedactSecrets(m));
        }

        var combined = string.Join(" → ", parts);
        if (combined.Length > 2500)
            combined = combined[..2500] + "…";
        return string.IsNullOrEmpty(combined) ? "See server logs." : combined;
    }

    private static async Task AppendLogAsync(ApplicationDbContext db, Guid id, string line)
    {
        var row = await db.RepoMigrations.FirstAsync(m => m.Id == id).ConfigureAwait(false);
        var ts = DateTimeOffset.UtcNow.ToString("u");
        var next = string.IsNullOrEmpty(row.Logs) ? $"[{ts}] {line}" : $"{row.Logs}\n[{ts}] {line}";
        if (next.Length > 15500)
            next = next[^15500..];
        row.Logs = next;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static async Task FailAsync(ApplicationDbContext db, Guid id, string message)
    {
        var row = await db.RepoMigrations.FirstAsync(m => m.Id == id).ConfigureAwait(false);
        var ts = DateTimeOffset.UtcNow.ToString("u");
        var next = string.IsNullOrEmpty(row.Logs) ? $"[{ts}] {message}" : $"{row.Logs}\n[{ts}] {message}";
        if (next.Length > 15500)
            next = next[^15500..];
        row.Logs = next;
        row.Status = MigrationStatus.Failed;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync().ConfigureAwait(false);
    }
}
