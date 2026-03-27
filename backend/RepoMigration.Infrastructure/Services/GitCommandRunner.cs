using System.Diagnostics;

namespace RepoMigration.Infrastructure.Services;

internal static class GitCommandRunner
{
    public static async Task<(int ExitCode, string CombinedOutput)> RunAsync(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var readOut = process.StandardOutput.ReadToEndAsync();
        var readErr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await readOut.ConfigureAwait(false);
        var stderr = await readErr.ConfigureAwait(false);
        var combined = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return (process.ExitCode, combined.Trim());
    }
}
