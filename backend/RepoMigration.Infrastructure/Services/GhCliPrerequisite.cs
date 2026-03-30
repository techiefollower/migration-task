namespace RepoMigration.Infrastructure.Services;

/// <summary>
/// Fails fast when configured GitHub CLI path points at a missing file.
/// Bare <c>gh</c> is allowed: <see cref="GhCliRunner"/> merges Machine+User PATH for the child process.
/// </summary>
internal static class GhCliPrerequisite
{
    internal static void ThrowIfHostCannotRunGh(IGhCliPathResolver resolver)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var path = resolver.ResolveGhExecutablePath();
        if (string.Equals(path, "gh", StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(path))
        {
            throw new ArgumentException(
                $"GitHub CLI path does not exist: \"{path}\". Fix GhCli:ExecutablePath in appsettings.json or GH_CLI_PATH.");
        }
    }
}
