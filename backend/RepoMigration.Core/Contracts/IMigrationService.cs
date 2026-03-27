using RepoMigration.Core.Dtos;

namespace RepoMigration.Core.Contracts;

public interface IMigrationService
{
    Task<QueueMigrationsResponse> QueueAsync(QueueMigrationsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MigrationListItemDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<MigrationsSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<bool> RetryAsync(Guid id, RetryMigrationRequest request, CancellationToken cancellationToken = default);
}
