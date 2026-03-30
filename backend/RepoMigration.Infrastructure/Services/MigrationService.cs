using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;
using RepoMigration.Core.Enums;
using RepoMigration.Core.Entities;
using RepoMigration.Infrastructure.Data;

namespace RepoMigration.Infrastructure.Services;

public sealed class MigrationService : IMigrationService
{
    private static readonly Regex TargetRepoNameRegex = new("^[a-zA-Z0-9._-]{1,100}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedVisibilities = new(StringComparer.OrdinalIgnoreCase) { "private", "public", "internal" };

    private readonly ApplicationDbContext _db;
    private readonly IBackgroundJobClient _backgroundJobs;
    private readonly IGhCliPathResolver _ghCliPath;
    private readonly ILogger<MigrationService> _logger;

    public MigrationService(
        ApplicationDbContext db,
        IBackgroundJobClient backgroundJobs,
        IGhCliPathResolver ghCliPath,
        ILogger<MigrationService> logger)
    {
        _db = db;
        _backgroundJobs = backgroundJobs;
        _ghCliPath = ghCliPath;
        _logger = logger;
    }

    public async Task<QueueMigrationsResponse> QueueAsync(QueueMigrationsRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AdoPersonalAccessToken) ||
            string.IsNullOrWhiteSpace(request.GitHubPersonalAccessToken) ||
            string.IsNullOrWhiteSpace(request.GitHubOwner))
            throw new ArgumentException("Tokens and GitHub owner are required.");

        if (request.Repositories == null || request.Repositories.Count == 0)
            throw new ArgumentException("Select at least one repository.");

        GhCliPrerequisite.ThrowIfHostCannotRunGh(_ghCliPath);

        var queued = new List<QueuedMigrationDto>();
        foreach (var item in request.Repositories)
        {
            if (string.IsNullOrWhiteSpace(item.SourceRemoteUrl) || string.IsNullOrWhiteSpace(item.TargetRepoName))
                continue;

            if (!TargetRepoNameRegex.IsMatch(item.TargetRepoName.Trim()))
                throw new ArgumentException($"Invalid target repository name: '{item.TargetRepoName}'.");

            var visibilityRaw = string.IsNullOrWhiteSpace(item.TargetRepoVisibility)
                ? "private"
                : item.TargetRepoVisibility.Trim().ToLowerInvariant();
            if (!AllowedVisibilities.Contains(visibilityRaw))
                throw new ArgumentException($"Invalid target-repo-visibility '{item.TargetRepoVisibility}'. Use private, public, or internal.");

            var pipeline = string.IsNullOrWhiteSpace(item.AdoPipeline) ? null : item.AdoPipeline.Trim();
            var serviceConn = string.IsNullOrWhiteSpace(item.ServiceConnectionId) ? null : item.ServiceConnectionId.Trim();
            if (pipeline != null ^ serviceConn != null)
                throw new ArgumentException("For pipeline rewiring, provide both adoPipeline and serviceConnectionId, or leave both empty.");

            var targetUrl = $"https://github.com/{request.GitHubOwner.Trim()}/{item.TargetRepoName.Trim()}.git";
            var entity = new RepoMigrationRecord
            {
                Id = Guid.NewGuid(),
                RepoName = item.TargetRepoName.Trim(),
                SourceUrl = item.SourceRemoteUrl.Trim(),
                TargetUrl = targetUrl,
                TargetRepoVisibility = visibilityRaw,
                AdoPipeline = pipeline,
                ServiceConnectionId = serviceConn,
                Status = MigrationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _db.RepoMigrations.Add(entity);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _backgroundJobs.Enqueue<MigrationJobExecutor>(j =>
                j.RunAsync(entity.Id, request.AdoPersonalAccessToken, request.GitHubPersonalAccessToken, request.GitHubOwner.Trim()));

            queued.Add(new QueuedMigrationDto(entity.Id, entity.RepoName, entity.TargetUrl));
            _logger.LogInformation("Queued migration {MigrationId} for repository {RepoName}", entity.Id, entity.RepoName);
        }

        return new QueueMigrationsResponse(queued);
    }

    public async Task<IReadOnlyList<MigrationListItemDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.RepoMigrations
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => new MigrationListItemDto(
            r.Id,
            r.RepoName,
            r.SourceUrl,
            r.TargetUrl,
            r.Status.ToString(),
            r.Logs,
            r.CreatedAt,
            r.UpdatedAt)).ToList();
    }

    public async Task<MigrationsSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var grouped = await _db.RepoMigrations
            .AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = grouped.FirstOrDefault(x => x.Status == MigrationStatus.Pending)?.Count ?? 0;
        var inProgress = grouped.FirstOrDefault(x => x.Status == MigrationStatus.InProgress)?.Count ?? 0;
        var completed = grouped.FirstOrDefault(x => x.Status == MigrationStatus.Completed)?.Count ?? 0;
        var failed = grouped.FirstOrDefault(x => x.Status == MigrationStatus.Failed)?.Count ?? 0;
        var total = pending + inProgress + completed + failed;
        return new MigrationsSummaryDto(pending, inProgress, completed, failed, total);
    }

    public async Task<bool> RetryAsync(Guid id, RetryMigrationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AdoPersonalAccessToken) ||
            string.IsNullOrWhiteSpace(request.GitHubPersonalAccessToken) ||
            string.IsNullOrWhiteSpace(request.GitHubOwner))
            return false;

        var entity = await _db.RepoMigrations.FirstOrDefaultAsync(m => m.Id == id, cancellationToken).ConfigureAwait(false);
        if (entity == null || entity.Status != MigrationStatus.Failed)
            return false;

        GhCliPrerequisite.ThrowIfHostCannotRunGh(_ghCliPath);

        entity.Status = MigrationStatus.Pending;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _backgroundJobs.Enqueue<MigrationJobExecutor>(j =>
            j.RunAsync(entity.Id, request.AdoPersonalAccessToken, request.GitHubPersonalAccessToken, request.GitHubOwner.Trim()));

        _logger.LogInformation("Re-queued migration {MigrationId}", id);
        return true;
    }
}
