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
            if (!LooksLikePath(c))
                return c;
        }

        if (OperatingSystem.IsWindows())
        {
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

            var fromPath = TryFindGhOnWindowsPath();
            if (fromPath != null)
                return fromPath;
        }

        return "gh";
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
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "scoop",
            "shims");

        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "chocolatey",
            "bin");
    }
}
