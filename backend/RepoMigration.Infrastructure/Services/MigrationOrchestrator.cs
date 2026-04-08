using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Infrastructure.Services;

public sealed class MigrationOrchestrator : IMigrationOrchestrator
{
    private static readonly Regex TargetRepoNameRegex = new("^[a-zA-Z0-9._-]{1,100}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedVisibilities = new(StringComparer.OrdinalIgnoreCase) { "private", "public", "internal" };

    private readonly IGhCliPathResolver _ghCliPath;
    private readonly IGitHubService _github;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MigrationOrchestrator> _logger;

    public MigrationOrchestrator(
        IGhCliPathResolver ghCliPath,
        IGitHubService github,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<MigrationOrchestrator> logger)
    {
        _ghCliPath = ghCliPath;
        _github = github;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ExecuteMigrationsResponse> ExecuteAsync(
        ExecuteMigrationsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AdoPersonalAccessToken) ||
            string.IsNullOrWhiteSpace(request.GitHubPersonalAccessToken) ||
            string.IsNullOrWhiteSpace(request.GitHubOwner))
            throw new ArgumentException("ADO PAT, GitHub PAT, and GitHub organization are required.");

        if (request.Repositories == null || request.Repositories.Count == 0)
            throw new ArgumentException("Select at least one repository.");

        GhCliPrerequisite.ThrowIfHostCannotRunGh(_ghCliPath);

        var owner = request.GitHubOwner.Trim();
        var visibilityRaw = string.IsNullOrWhiteSpace(request.TargetRepoVisibility)
            ? "private"
            : request.TargetRepoVisibility.Trim().ToLowerInvariant();
        if (!AllowedVisibilities.Contains(visibilityRaw))
            throw new ArgumentException("Invalid target-repo-visibility. Use private, public, or internal.");

        var items = new List<(string AdoName, MigrationJobParams Job)>();
        foreach (var item in request.Repositories)
        {
            if (string.IsNullOrWhiteSpace(item.SourceRemoteUrl) || string.IsNullOrWhiteSpace(item.TargetRepoName))
                continue;

            var name = item.TargetRepoName.Trim();
            if (!TargetRepoNameRegex.IsMatch(name))
                throw new ArgumentException($"Invalid target repository name: '{item.TargetRepoName}'.");

            var adoName = string.IsNullOrWhiteSpace(item.AdoRepoName) ? name : item.AdoRepoName.Trim();
            items.Add((adoName, new MigrationJobParams(
                item.SourceRemoteUrl.Trim(),
                name,
                visibilityRaw,
                request.AdoPersonalAccessToken,
                request.GitHubPersonalAccessToken,
                owner)));
        }

        if (items.Count == 0)
            throw new ArgumentException("No valid repositories to migrate (check URLs and target names).");

        var targetNames = items.Select(i => i.Job.TargetRepoName).ToList();
        if (targetNames.Count != targetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new ArgumentException("Duplicate target repository names in your selection.");

        var ghVal = await _github.ValidateTokenAsync(request.GitHubPersonalAccessToken, cancellationToken).ConfigureAwait(false);
        if (!ghVal.Valid)
            throw new ArgumentException(ghVal.Error ?? "Invalid GitHub PAT.");

        var ownerKind = await _github.GetOwnerKindAsync(
                request.GitHubPersonalAccessToken,
                owner,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(ownerKind.Kind, "organization", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(ownerKind.Hint ?? "GitHub owner must be an organization for ado2gh migrate-repo.");

        var check = await _github.CheckRepositoriesExistAsync(
                request.GitHubPersonalAccessToken,
                owner,
                targetNames,
                cancellationToken)
            .ConfigureAwait(false);
        if (!check.Valid)
            throw new ArgumentException(check.Error ?? "Could not verify repository names on GitHub.");

        var taken = check.Results!
            .Where(r => r.Exists)
            .Select(r => r.Name)
            .ToList();
        if (taken.Count > 0)
            throw new ArgumentException(
                "These repository names already exist on GitHub: " + string.Join(", ", taken) + ". Rename them and try again.");

        var skipPatPerJob = true;
        var results = await RunJobsWithResultsAsync(items, skipPatPerJob, cancellationToken).ConfigureAwait(false);
        var allOk = results.All(r => r.Success);
        return new ExecuteMigrationsResponse(results, allOk);
    }

    private async Task<List<ExecutedRepoResultDto>> RunJobsWithResultsAsync(
        IReadOnlyList<(string AdoName, MigrationJobParams Job)> items,
        bool skipGitHubPatValidation,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return new List<ExecutedRepoResultDto>();

        var max = Math.Clamp(_configuration.GetValue("Migration:MaxConcurrentJobs", 2), 1, 8);

        async Task<ExecutedRepoResultDto> RunOne((string AdoName, MigrationJobParams Job) row)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<MigrationJobExecutor>();
            MigrationJobResult outcome;
            try
            {
                outcome = await executor.RunAsync(row.Job, skipGitHubPatValidation, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration failed for {Target}", row.Job.TargetRepoName);
                outcome = new MigrationJobResult(false, string.Empty, ex.Message);
            }

            var tail = string.IsNullOrEmpty(outcome.Logs)
                ? null
                : outcome.Logs.Length > 1200
                    ? outcome.Logs[^1200..]
                    : outcome.Logs;

            return new ExecutedRepoResultDto(
                row.AdoName,
                row.Job.TargetRepoName,
                outcome.Success,
                outcome.ErrorMessage,
                tail);
        }

        if (max == 1)
        {
            var list = new List<ExecutedRepoResultDto>();
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                list.Add(await RunOne(item).ConfigureAwait(false));
            }

            return list;
        }

        using var gate = new SemaphoreSlim(max, max);
        var tasks = items.Select(async row =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await RunOne(row).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });
        var arr = await Task.WhenAll(tasks).ConfigureAwait(false);
        return arr.ToList();
    }
}
