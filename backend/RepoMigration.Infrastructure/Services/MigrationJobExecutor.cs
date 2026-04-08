using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Infrastructure.Services;

public class MigrationJobExecutor
{
    private static readonly string[] AllowedVisibilities = ["private", "public", "internal"];

    private const string GhNotFoundUserHint =
        "[rm-api-gh] GitHub CLI could not be started. Install: winget install GitHub.cli — then set RepoMigration.Api/appsettings.json GhCli:ExecutablePath to the output of PowerShell (Get-Command gh).Source (double backslashes in JSON), or set GH_CLI_PATH, and restart the API. If you do NOT see [rm-api-gh] at the start of this message, stop the API, delete bin/obj folders, rebuild RepoMigration.Api, and run again.";

    private readonly IGhCliPathResolver _ghCliPath;
    private readonly IConfiguration _configuration;
    private readonly IGitHubService _github;
    private readonly ILogger<MigrationJobExecutor> _logger;

    public MigrationJobExecutor(
        IGhCliPathResolver ghCliPath,
        IConfiguration configuration,
        IGitHubService github,
        ILogger<MigrationJobExecutor> logger)
    {
        _ghCliPath = ghCliPath;
        _configuration = configuration;
        _github = github;
        _logger = logger;
    }

    public async Task<MigrationJobResult> RunAsync(
        MigrationJobParams job,
        bool skipGitHubPatValidation,
        CancellationToken cancellationToken = default)
    {
        var log = new StringBuilder();
        var workId = Guid.NewGuid().ToString("N");
        var workRoot = Path.Combine(Path.GetTempPath(), "repo-migration", "ado2gh", workId);

        void Append(string line)
        {
            var ts = DateTimeOffset.UtcNow.ToString("u");
            log.Append('[').Append(ts).Append("] ").AppendLine(line.TrimEnd());
        }

        try
        {
            if (!skipGitHubPatValidation)
            {
                Append("Validating GitHub token…");
                var validation = await _github.ValidateTokenAsync(job.GitHubPersonalAccessToken, cancellationToken).ConfigureAwait(false);
                if (!validation.Valid || string.IsNullOrWhiteSpace(validation.Login))
                    return Fail(log, validation.Error ?? "GitHub token invalid.");
            }

            if (!AzureDevOpsGitUrlParser.TryParse(job.SourceRemoteUrl, out var adoOrg, out var adoProject, out var adoRepo))
            {
                return Fail(
                    log,
                    "Could not parse Azure DevOps Git URL for gh ado2gh. Expected https://dev.azure.com/{org}/{project}/_git/{repo} or https://{org}.visualstudio.com/…");
            }

            var visibility = AllowedVisibilities.Contains(job.TargetRepoVisibility?.Trim().ToLowerInvariant())
                ? job.TargetRepoVisibility!.Trim().ToLowerInvariant()
                : "private";

            Directory.CreateDirectory(workRoot);

            var ghExe = _ghCliPath.ResolveGhExecutablePath();
            if (ghExe != "gh" && !File.Exists(ghExe))
            {
                return Fail(
                    log,
                    $"GitHub CLI not found at GhCli:ExecutablePath '{ghExe}'. Fix appsettings or install GitHub CLI.");
            }

            var trustQueuedPat = _configuration.GetValue("Migration:TrustQueuedGitHubPat", false);
            var skipInventory = _configuration.GetValue("GhAdo2Gh:SkipInventoryReport", true);
            var preMigrateLines = new List<string>
            {
                skipGitHubPatValidation || trustQueuedPat
                    ? "Skipped per-job GitHub PAT re-check (validated before migration batch)."
                    : "GitHub PAT validated.",
                $"Using GitHub CLI: {ghExe}",
                $"GhAdo2Gh:SkipInventoryReport={skipInventory} (set false only to run inventory; env: GhAdo2Gh__SkipInventoryReport).",
                "Note: gh ado2gh migrate-repo uses GitHub Enterprise Importer — large repos often take several minutes; this is normal.",
            };
            if (skipInventory)
            {
                preMigrateLines.Add(
                    "Step 1/2: Skipped inventory-report — migrate-repo does not need the CSV (enable only if you need ADO pipeline inventory; PAT needs Build: Read).");
            }

            foreach (var line in preMigrateLines)
                Append(line);

            if (!skipInventory)
            {
                Append($"Step 1/2: gh ado2gh inventory-report for org '{adoOrg}'…");
                var inventoryArgs = new List<string> { "ado2gh", "inventory-report", "--ado-org", adoOrg };
                var (invCode, invOut) = await GhCliRunner.RunAsync(ghExe, workRoot, inventoryArgs, job.AdoPersonalAccessToken, job.GitHubPersonalAccessToken).ConfigureAwait(false);
                Append(SanitizeGhOutput("inventory-report finished.", invOut));
                if (invCode != 0)
                {
                    return Fail(
                        log,
                        $"gh ado2gh inventory-report failed (exit {invCode}). " +
                        "This often fails at \"Finding Pipelines\" when the ADO PAT lacks Build (Read) or Azure returns a pipeline API error. " +
                        "Set GhAdo2Gh:SkipInventoryReport to true in appsettings.json to run migrate-repo without inventory (recommended for this app). " +
                        $"Or grant Build: Read on the PAT and retry.{Ado2GhExtensionInstallHint(invOut)}");
                }
            }

            Append(
                $"Step 2/2: gh ado2gh migrate-repo — ADO {adoOrg}/{adoProject}/{adoRepo} → GitHub {job.GitHubOwner.Trim()}/{job.TargetRepoName} ({visibility})…");
            var migrateArgs = new List<string>
            {
                "ado2gh", "migrate-repo",
                "--ado-org", adoOrg,
                "--ado-team-project", adoProject,
                "--ado-repo", adoRepo,
                "--github-org", job.GitHubOwner.Trim(),
                "--github-repo", job.TargetRepoName,
                "--target-repo-visibility", visibility,
            };
            var (migCode, migOut) = await GhCliRunner.RunAsync(ghExe, workRoot, migrateArgs, job.AdoPersonalAccessToken, job.GitHubPersonalAccessToken).ConfigureAwait(false);
            Append(SanitizeGhOutput("migrate-repo output:", migOut));

            var migrateOk = migCode == 0 && LooksLikeMigrateSucceeded(migOut);
            if (!migrateOk)
            {
                return Fail(log, BuildMigrateRepoFailureUserMessage(migCode, migOut));
            }

            Append("Migration completed successfully.");
            return TrimLogs(new MigrationJobResult(true, log.ToString(), null));
        }
        catch (OperationCanceledException)
        {
            Append("Cancelled.");
            throw;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2 && OperatingSystem.IsWindows())
        {
            _logger.LogError(ex, "GitHub CLI not found for migration job");
            return Fail(log, GhNotFoundUserHint);
        }
        catch (IOException ex) when (ex.Message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(ex, "GitHub CLI not found (IO) for migration job");
            return Fail(log, GhNotFoundUserHint);
        }
        catch (Exception ex) when (IsWindowsProcessStartFailureForGh(ex))
        {
            _logger.LogError(ex, "GitHub CLI process start failed for migration job");
            return Fail(log, GhNotFoundUserHint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration job failed");
            var detail = SanitizeErrorMessage(ex);
            return Fail(log, $"Unexpected error: {detail}");
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
                _logger.LogWarning(ex, "Could not delete temp ado2gh directory for job {WorkId}", workId);
            }
        }
    }

    private static MigrationJobResult Fail(StringBuilder log, string message)
    {
        var ts = DateTimeOffset.UtcNow.ToString("u");
        log.Append('[').Append(ts).Append("] ").AppendLine(message);
        return TrimLogs(new MigrationJobResult(false, log.ToString(), message));
    }

    private static MigrationJobResult TrimLogs(MigrationJobResult result)
    {
        const int maxLen = 20000;
        if (result.Logs.Length <= maxLen)
            return result;
        return result with { Logs = result.Logs[^maxLen..] };
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

    /// <summary>Puts redacted gh output into the API/UI error so operators see the real failure, not only exit code.</summary>
    private static string BuildMigrateRepoFailureUserMessage(int exitCode, string? migOut)
    {
        var raw = migOut ?? string.Empty;
        var cleaned = RedactSecrets(raw).Trim();
        var extHint = Ado2GhExtensionInstallHint(raw);

        string body;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            body =
                "No output was captured from GitHub CLI. On the machine running this API: run `gh version`, then `gh extension list` (expect github/gh-ado2gh), and run the same migrate-repo from a shell as the same user as the API process.";
        }
        else
        {
            const int max = 1800;
            var tail = cleaned.Length <= max ? cleaned : "…" + cleaned[^max..];
            body = tail;
        }

        var msg = $"gh ado2gh migrate-repo failed (exit {exitCode}). {body}";
        if (!string.IsNullOrEmpty(extHint))
            msg += extHint;

        if (raw.Contains("enterprise importer", StringComparison.OrdinalIgnoreCase) &&
            (raw.Contains("403", StringComparison.Ordinal) || raw.Contains("401", StringComparison.Ordinal) ||
             raw.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
             raw.Contains("denied", StringComparison.OrdinalIgnoreCase)))
        {
            msg +=
                " Often this is GitHub Enterprise Importer access: org admin must allow migrations, and the GitHub PAT needs scopes to create repos in the target org (e.g. repo, admin:org for classic PATs).";
        }

        if (raw.Contains("CreateMigrationSource", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("migrator role", StringComparison.OrdinalIgnoreCase))
        {
            msg +=
                " [Action] Enterprise Importer is stricter than normal org membership: you need org Owner or the Migrator role (Member alone is not enough). Ask an org owner to grant Migrator under Enterprise Importer settings. If the org uses SAML SSO, authorize this PAT for the org (GitHub → Settings → Applications). PAT also needs repo + metadata scopes as in GitHub’s GEI docs.";
        }

        if (msg.Length > 2800)
            msg = msg[..2800] + "…";
        return msg;
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
}
