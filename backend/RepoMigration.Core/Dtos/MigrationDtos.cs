namespace RepoMigration.Core.Dtos;

public record QueueMigrationItemDto(
    string SourceRemoteUrl,
    string TargetRepoName,
    string? TargetRepoVisibility = null,
    string? AdoPipeline = null,
    string? ServiceConnectionId = null);

public record QueueMigrationsRequest(
    string AdoPersonalAccessToken,
    string GitHubPersonalAccessToken,
    string GitHubOwner,
    IReadOnlyList<QueueMigrationItemDto> Repositories);

public record QueuedMigrationDto(Guid Id, string RepoName, string TargetUrl);

public record QueueMigrationsResponse(IReadOnlyList<QueuedMigrationDto> Queued);

public record MigrationListItemDto(
    Guid Id,
    string RepoName,
    string SourceUrl,
    string TargetUrl,
    string Status,
    string? Logs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record MigrationsSummaryDto(int Pending, int InProgress, int Completed, int Failed, int Total);

public record RetryMigrationRequest(
    string AdoPersonalAccessToken,
    string GitHubPersonalAccessToken,
    string GitHubOwner);
