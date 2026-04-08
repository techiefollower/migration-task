namespace RepoMigration.Core.Dtos;

/// <summary>One repository the user chose to migrate, with optional ADO display name.</summary>
public record MigrateRepositoryItemDto(
    string SourceRemoteUrl,
    string TargetRepoName,
    string? AdoRepoName = null);

public record ExecuteMigrationsRequest(
    string AdoPersonalAccessToken,
    string GitHubPersonalAccessToken,
    string GitHubOwner,
    IReadOnlyList<MigrateRepositoryItemDto> Repositories,
    string? TargetRepoVisibility = null);

public record ExecutedRepoResultDto(
    string AdoRepoName,
    string TargetRepoName,
    bool Success,
    string? Error,
    string? LogsTail);

public record ExecuteMigrationsResponse(
    IReadOnlyList<ExecutedRepoResultDto> Results,
    bool AllSucceeded);

/// <summary>Parameters for a single <c>gh ado2gh migrate-repo</c> run.</summary>
public record MigrationJobParams(
    string SourceRemoteUrl,
    string TargetRepoName,
    string TargetRepoVisibility,
    string AdoPersonalAccessToken,
    string GitHubPersonalAccessToken,
    string GitHubOwner);

/// <summary>Outcome of one migration job (in-memory; not persisted).</summary>
public record MigrationJobResult(bool Success, string Logs, string? ErrorMessage);
