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
}
