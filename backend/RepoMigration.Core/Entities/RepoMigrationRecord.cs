using RepoMigration.Core.Enums;

namespace RepoMigration.Core.Entities;

/// <summary>
/// Persisted migration row; maps to SQL table <c>RepoMigration</c>.
/// </summary>
public class RepoMigrationRecord
{
    public Guid Id { get; set; }
    public string RepoName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    /// <summary>GitHub visibility passed to <c>gh ado2gh migrate-repo --target-repo-visibility</c>.</summary>
    public string TargetRepoVisibility { get; set; } = "private";
    /// <summary>
    /// Optional ADO build pipeline for <c>gh ado2gh rewire-pipeline</c>: definition <b>name</b> or folder path (e.g. <c>\Services\CI</c>),
    /// or digits-only <b>definition id</b> from the pipeline URL (passed as <c>--ado-pipeline-id</c>).
    /// </summary>
    public string? AdoPipeline { get; set; }
    /// <summary>ADO service connection id (GUID) for rewire-pipeline.</summary>
    public string? ServiceConnectionId { get; set; }
    public MigrationStatus Status { get; set; }
    public string? Logs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
