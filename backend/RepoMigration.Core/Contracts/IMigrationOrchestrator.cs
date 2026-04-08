using RepoMigration.Core.Dtos;

namespace RepoMigration.Core.Contracts;

public interface IMigrationOrchestrator
{
    Task<ExecuteMigrationsResponse> ExecuteAsync(
        ExecuteMigrationsRequest request,
        CancellationToken cancellationToken = default);
}
