namespace RepoMigration.Infrastructure.Services;

/// <summary>
/// Resolves the GitHub CLI executable. The API host often lacks <c>gh</c> on PATH (unlike an interactive shell).
/// </summary>
public interface IGhCliPathResolver
{
    /// <summary>Full path to <c>gh.exe</c> when known, or <c>gh</c> to rely on PATH.</summary>
    string ResolveGhExecutablePath();
}
