namespace RepoMigration.Infrastructure.Services;

/// <summary>
/// Parses HTTPS Azure DevOps Git remote URLs into org, project, and repository name for gh ado2gh.
/// </summary>
internal static class AzureDevOpsGitUrlParser
{
    public static bool TryParse(string remoteUrl, out string org, out string project, out string repo)
    {
        org = project = repo = string.Empty;
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return false;

        if (!Uri.TryCreate(remoteUrl.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4)
            return false;

        var gitIndex = Array.FindIndex(segments, s => s.Equals("_git", StringComparison.OrdinalIgnoreCase));
        if (gitIndex < 1 || gitIndex >= segments.Length - 1)
            return false;

        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            org = Uri.UnescapeDataString(segments[0]);
            project = Uri.UnescapeDataString(string.Join("/", segments.Skip(1).Take(gitIndex - 1)));
            repo = NormalizeRepoName(segments[^1]);
            return !string.IsNullOrEmpty(org) && !string.IsNullOrEmpty(project) && !string.IsNullOrEmpty(repo);
        }

        if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
        {
            var hostOrg = uri.Host[..uri.Host.IndexOf(".visualstudio.com", StringComparison.OrdinalIgnoreCase)];
            org = hostOrg;
            project = Uri.UnescapeDataString(string.Join("/", segments.Take(gitIndex)));
            repo = NormalizeRepoName(segments[^1]);
            return !string.IsNullOrEmpty(org) && !string.IsNullOrEmpty(project) && !string.IsNullOrEmpty(repo);
        }

        return false;
    }

    private static string NormalizeRepoName(string segment)
    {
        var name = Uri.UnescapeDataString(segment);
        return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }
}
