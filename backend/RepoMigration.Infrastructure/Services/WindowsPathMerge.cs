using System.Diagnostics;
using System.Linq;

namespace RepoMigration.Infrastructure.Services;

/// <summary>
/// Builds merged PATH / PATHEXT for child processes. VS often starts the API with an incomplete user PATH;
/// duplicate <c>Path</c>/<c>PATH</c> keys can also leave the wrong value active.
/// </summary>
internal static class WindowsPathMerge
{
    internal static string BuildMergedPath()
    {
        if (!OperatingSystem.IsWindows())
            return Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        void AddSegments(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
                return;
            foreach (var piece in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = Environment.ExpandEnvironmentVariables(piece.Trim());
                if (s.Length == 0 || !seen.Add(s))
                    continue;
                parts.Add(s);
            }
        }

        AddSegments(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
        AddSegments(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
        AddSegments(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process));

        return string.Join(';', parts);
    }

    internal static string BuildMergedPathext()
    {
        if (!OperatingSystem.IsWindows())
            return Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        void AddSegments(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
                return;
            foreach (var piece in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var s = piece.Trim();
                if (s.Length == 0 || !seen.Add(s))
                    continue;
                parts.Add(s);
            }
        }

        AddSegments(Environment.GetEnvironmentVariable("PATHEXT", EnvironmentVariableTarget.Machine));
        AddSegments(Environment.GetEnvironmentVariable("PATHEXT", EnvironmentVariableTarget.User));
        AddSegments(Environment.GetEnvironmentVariable("PATHEXT", EnvironmentVariableTarget.Process));

        return parts.Count > 0
            ? string.Join(';', parts)
            : ".COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC";
    }

    /// <summary>Removes all PATH / PATHEXT keys (any casing) and sets merged values.</summary>
    internal static void ApplyMergedPathAndPathext(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsWindows())
            return;

        RemoveEnvKeysIgnoringCase(psi, "PATH");
        psi.Environment["PATH"] = BuildMergedPath();

        RemoveEnvKeysIgnoringCase(psi, "PATHEXT");
        psi.Environment["PATHEXT"] = BuildMergedPathext();
    }

    private static void RemoveEnvKeysIgnoringCase(ProcessStartInfo psi, string name)
    {
        foreach (var key in psi.Environment.Keys.Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToList())
            psi.Environment.Remove(key);
    }
}
