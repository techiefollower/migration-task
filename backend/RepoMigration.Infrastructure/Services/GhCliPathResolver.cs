using System.Collections;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;

namespace RepoMigration.Infrastructure.Services;

public sealed class GhCliPathResolver : IGhCliPathResolver
{
    private static readonly string[] GhFileNames = ["gh.exe", "gh.cmd", "gh.bat"];

    private readonly string? _configured;

    public GhCliPathResolver(IConfiguration configuration)
    {
        _configured = configuration["GhCli:ExecutablePath"];
    }

    public string ResolveGhExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_configured))
        {
            var c = _configured.Trim().Trim('"');
            if (File.Exists(c))
                return Path.GetFullPath(c);
            // Misconfigured full path — do not fall through and pick another install.
            if (LooksLikePath(c))
                return c;
            // "gh" / "gh.exe" without a directory means "auto-discover", not "return early and skip heuristics".
            if (!string.Equals(c, "gh", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c, "gh.exe", StringComparison.OrdinalIgnoreCase))
                return c;
        }

        if (OperatingSystem.IsWindows())
        {
            var fromEnv = TryFindGhFromGhCliPathEnv();
            if (fromEnv != null)
                return fromEnv;

            foreach (var dir in StandardWindowsInstallDirs())
            {
                var found = FindGhInDirectory(dir);
                if (found != null)
                    return found;
            }

            var npmDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
            var fromNpm = FindGhInDirectory(npmDir);
            if (fromNpm != null)
                return fromNpm;

            var dotnetGh = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".dotnet",
                "tools",
                "gh.exe");
            if (File.Exists(dotnetGh))
                return Path.GetFullPath(dotnetGh);

            var fromReg = TryFindGhInAppPathsRegistry();
            if (fromReg != null)
                return fromReg;

            var fromWinGet = TryFindGhUnderWinGetPackages();
            if (fromWinGet != null)
                return fromWinGet;

            var fromPath = TryFindGhOnWindowsPath();
            if (fromPath != null)
                return fromPath;

            var viaWhere = TryResolveGhUsingWhereExe();
            if (viaWhere != null)
                return viaWhere;
        }

        return "gh";
    }

    /// <summary>
    /// Uses <c>where.exe</c> with a merged Machine+User PATH — finds <c>gh</c> when the API process PATH is incomplete.
    /// </summary>
    private static string? TryResolveGhUsingWhereExe()
    {
        var whereExe = Path.Combine(Environment.SystemDirectory, "where.exe");
        if (!File.Exists(whereExe))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = whereExe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (DictionaryEntry kv in Environment.GetEnvironmentVariables())
            {
                var key = kv.Key?.ToString();
                if (string.IsNullOrEmpty(key))
                    continue;
                psi.Environment[key] = kv.Value?.ToString() ?? string.Empty;
            }

            WindowsPathMerge.ApplyMergedPathAndPathext(psi);
            psi.ArgumentList.Add("gh");

            using var p = new Process { StartInfo = psi };
            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                return null;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (t.Length == 0)
                    continue;
                if (File.Exists(t))
                    return Path.GetFullPath(t);
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>winget installs GitHub CLI under LocalAppData\Microsoft\WinGet\Packages\GitHub.cli_* (not always on PATH for the API process).</summary>
    private static string? TryFindGhUnderWinGetPackages()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WinGet",
            "Packages");
        if (!Directory.Exists(root))
            return null;

        try
        {
            foreach (var pkgDir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(pkgDir);
                if (name.IndexOf("GitHub.cli", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var found = FindGhExeRecursive(pkgDir, maxDepth: 4);
                if (found != null)
                    return found;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // ignore
        }

        return null;
    }

    private static string? FindGhExeRecursive(string dir, int maxDepth)
    {
        if (maxDepth < 0 || !Directory.Exists(dir))
            return null;
        try
        {
            var direct = Path.Combine(dir, "gh.exe");
            if (File.Exists(direct))
                return Path.GetFullPath(direct);
            if (maxDepth == 0)
                return null;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var inner = FindGhExeRecursive(sub, maxDepth - 1);
                if (inner != null)
                    return inner;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // ignore
        }

        return null;
    }

    /// <summary>Optional override: set <c>GH_CLI_PATH</c> to the full path of <c>gh.exe</c> / <c>gh.cmd</c> for the API process (User or Machine).</summary>
    private static string? TryFindGhFromGhCliPathEnv()
    {
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine,
                 })
        {
            var raw = Environment.GetEnvironmentVariable("GH_CLI_PATH", target);
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var p = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
            if (File.Exists(p))
                return Path.GetFullPath(p);
        }

        return null;
    }

    private static string? FindGhInDirectory(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;
        foreach (var name in GhFileNames)
        {
            try
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
                // ignore
            }
        }

        return null;
    }

    private static bool LooksLikePath(string value) =>
        value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

    private static string? TryFindGhInAppPathsRegistry()
    {
#pragma warning disable CA1416 // Registry is Windows-only; caller is inside OperatingSystem.IsWindows()
        foreach (var keyPath in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\gh.exe",
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\gh.exe",
                 })
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var k = hive.OpenSubKey(keyPath);
                var def = k?.GetValue(null) as string;
                if (string.IsNullOrEmpty(def))
                    continue;
                var expanded = Environment.ExpandEnvironmentVariables(def);
                if (File.Exists(expanded))
                    return Path.GetFullPath(expanded);
            }
        }

        return null;
#pragma warning restore CA1416
    }

    /// <summary>
    /// API hosts (VS, IIS) often do not load the user PATH; read Machine + User + Process explicitly.
    /// </summary>
    private static string? TryFindGhOnWindowsPath()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Machine,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Process,
                 })
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH", target);
            if (string.IsNullOrEmpty(pathVar))
                continue;

            foreach (var piece in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = Environment.ExpandEnvironmentVariables(piece.Trim().Trim('"'));
                if (dir.Length == 0 || !seen.Add(dir))
                    continue;

                var found = FindGhInDirectory(dir);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static IEnumerable<string> StandardWindowsInstallDirs()
    {
        // 32-bit API on 64-bit Windows: ProgramFiles is (x86); GitHub CLI is often under native Program Files.
        var programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        if (!string.IsNullOrEmpty(programW6432))
            yield return Path.Combine(programW6432, "GitHub CLI");

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI");
        var pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrEmpty(pf86))
            yield return Path.Combine(pf86, "GitHub CLI");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "GitHub CLI");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WinGet",
            "Links");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "scoop",
            "shims");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "chocolatey",
            "bin");
    }
}
