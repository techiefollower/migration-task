using RepoMigration.Core.Dtos;

namespace RepoMigration.Core.Contracts;

public interface IAdoDevOpsService
{
    Task<AdoProjectsResponse> GetProjectsAsync(string organization, string personalAccessToken, CancellationToken cancellationToken = default);

    Task<AdoRepositoriesResponse> GetRepositoriesAsync(
        string organization,
        string projectIdOrName,
        string personalAccessToken,
        CancellationToken cancellationToken = default);
}
