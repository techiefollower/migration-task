namespace RepoMigration.Core.Dtos;

/// <summary>
/// Runs <c>gh ado2gh rewire-pipeline</c> only (no migrate-repo). Use when the GitHub repo already exists.
/// </summary>
public record RewirePipelineRequest(
    string AdoOrganization,
    string AdoTeamProject,
    /// <summary>Build definition name/path, or digits-only <c>definitionId</c> from the pipeline URL.</summary>
    string AdoPipeline,
    string GitHubOrganization,
    string GitHubRepository,
    string ServiceConnectionId,
    string AdoPersonalAccessToken,
    string GitHubPersonalAccessToken);

public record RewirePipelineResponse(bool Success, int ExitCode, string Output);
