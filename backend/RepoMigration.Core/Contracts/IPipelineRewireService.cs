using RepoMigration.Core.Dtos;

namespace RepoMigration.Core.Contracts;

public interface IPipelineRewireService
{
    Task<RewirePipelineResponse> RewireAsync(RewirePipelineRequest request, CancellationToken cancellationToken = default);
}
