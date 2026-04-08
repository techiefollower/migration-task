using RepoMigration.Core.Dtos;

namespace RepoMigration.Core.Contracts;

public interface IGitHubService
{
    Task<GitHubValidateResponse> ValidateTokenAsync(string personalAccessToken, CancellationToken cancellationToken = default);

    Task<GitHubCheckReposResponse> CheckRepositoriesExistAsync(
        string personalAccessToken,
        string owner,
        IReadOnlyList<string> repoNames,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string? Error)> CreatePrivateRepositoryAsync(
        string personalAccessToken,
        string owner,
        string authenticatedLogin,
        string repoName,
        CancellationToken cancellationToken = default);

    /// <summary>Uses GET /orgs/{login} and GET /users/{login} to detect whether <paramref name="owner"/> is an org or personal user.</summary>
    Task<GitHubOwnerKindResponse> GetOwnerKindAsync(string personalAccessToken, string owner, CancellationToken cancellationToken = default);
}
