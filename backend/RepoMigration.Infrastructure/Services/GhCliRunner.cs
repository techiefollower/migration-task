using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RepoMigration.Infrastructure.Services;

internal static class GhCliRunner
{
    /// <summary>
    /// Runs <c>gh</c> with the given arguments. Injects ADO_PAT, GH_PAT, and GH_TOKEN for non-interactive ado2gh.
    /// </summary>
    public static async Task<(int ExitCode, string CombinedOutput)> RunAsync(
        string ghExecutablePath,
        string workingDirectory,
        IReadOnlyList<string> argumentList,
        string adoPat,
        string githubPat,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var kv in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
        {
            var key = kv.Key?.ToString();
            if (string.IsNullOrEmpty(key))
                continue;
            psi.Environment[key] = kv.Value?.ToString() ?? string.Empty;
        }

        psi.Environment["ADO_PAT"] = adoPat;
        psi.Environment["GH_PAT"] = githubPat;
        psi.Environment["GH_TOKEN"] = githubPat;
        psi.Environment["CI"] = "1";

        var ext = Path.GetExtension(ghExecutablePath);
        var bareGhOnWindows = OperatingSystem.IsWindows() &&
            string.Equals(ghExecutablePath, "gh", StringComparison.OrdinalIgnoreCase);
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            bareGhOnWindows)
        {
            var program = bareGhOnWindows ? "gh" : ghExecutablePath;
            var comspec = Environment.GetEnvironmentVariable("ComSpec")
                ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.FileName = comspec;
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(BuildCmdCCommandLine(program, argumentList));
        }
        else
        {
            psi.FileName = ghExecutablePath;
            foreach (var arg in argumentList)
                psi.ArgumentList.Add(arg);
        }

        WindowsPathMerge.ApplyMergedPathAndPathext(psi);

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

    /// <summary>Build one argument for <c>cmd.exe /c</c> (cmd quoting rules).</summary>
    private static string BuildCmdCCommandLine(string executable, IReadOnlyList<string> args)
    {
        var sb = new StringBuilder();
        sb.Append(QuoteForCmd(executable));
        foreach (var a in args)
        {
            sb.Append(' ');
            sb.Append(QuoteForCmd(a));
        }

        return sb.ToString();
    }

    private static string QuoteForCmd(string arg)
    {
        if (string.IsNullOrEmpty(arg))
            return "\"\"";
        if (arg.AsSpan().IndexOfAny([' ', '\t', '"']) < 0)
            return arg;
        return '"' + arg.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

}
