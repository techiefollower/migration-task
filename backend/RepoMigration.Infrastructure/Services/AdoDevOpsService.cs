using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Infrastructure.Services;

public sealed class AdoDevOpsService : IAdoDevOpsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AdoDevOpsService> _logger;

    public AdoDevOpsService(IHttpClientFactory httpClientFactory, ILogger<AdoDevOpsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<AdoProjectsResponse> GetProjectsAsync(
        string organization,
        string personalAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(personalAccessToken))
            return new AdoProjectsResponse(false, "Organization and PAT are required.", null);

        var client = CreateClient(personalAccessToken);
        var url = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/_apis/projects?api-version=7.1&$top=500";
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ADO projects request failed with status {StatusCode}", response.StatusCode);
            return new AdoProjectsResponse(false, "Could not load Azure DevOps projects. Check org name and PAT scopes (Code: Read, Project: Read).", null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var projects = new List<AdoProjectDto>();
        if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    projects.Add(new AdoProjectDto(id, name));
            }
        }

        return new AdoProjectsResponse(true, null, projects);
    }

    public async Task<AdoRepositoriesResponse> GetRepositoriesAsync(
        string organization,
        string projectIdOrName,
        string personalAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(projectIdOrName) ||
            string.IsNullOrWhiteSpace(personalAccessToken))
            return new AdoRepositoriesResponse(false, "Organization, project, and PAT are required.", null);

        var client = CreateClient(personalAccessToken);
        // Path segment: project GUID (recommended) or name — encode per RFC (spaces → %20).
        var projectSegment = Uri.EscapeDataString(projectIdOrName.Trim());
        var url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization.Trim())}/{projectSegment}/_apis/git/repositories?api-version=7.1&$top=500";
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ADO repositories request failed with status {StatusCode}", response.StatusCode);
            return new AdoRepositoriesResponse(false, "Could not load repositories for the selected project.", null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var repos = new List<AdoRepositoryDto>();
        if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.TryGetProperty("isDisabled", out var disEl) &&
                    disEl.ValueKind == JsonValueKind.True)
                    continue;

                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                    continue;

                var remote = ResolveGitRemoteUrl(item, organization.Trim(), projectIdOrName.Trim(), name);
                if (string.IsNullOrEmpty(remote))
                    continue;

                repos.Add(new AdoRepositoryDto(id, name, remote));
            }
        }

        return new AdoRepositoriesResponse(true, null, repos);
    }

    private HttpClient CreateClient(string personalAccessToken)
    {
        var client = _httpClientFactory.CreateClient("ado");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($":{personalAccessToken}")));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// ADO usually returns <c>remoteUrl</c>; some responses only have <c>webUrl</c>. Build HTTPS <c>_git</c> URL if needed so migrations are not dropped.
    /// </summary>
    private static string? ResolveGitRemoteUrl(
        JsonElement item,
        string organization,
        string projectIdOrName,
        string repositoryName)
    {
        if (item.TryGetProperty("remoteUrl", out var remoteEl) &&
            remoteEl.ValueKind == JsonValueKind.String)
        {
            var r = remoteEl.GetString();
            if (!string.IsNullOrWhiteSpace(r))
                return r.Trim();
        }

        if (item.TryGetProperty("webUrl", out var webEl) &&
            webEl.ValueKind == JsonValueKind.String)
        {
            var w = webEl.GetString();
            if (!string.IsNullOrWhiteSpace(w) &&
                w.Contains("_git", StringComparison.OrdinalIgnoreCase) &&
                w.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return w.Trim();
        }

        var orgSeg = Uri.EscapeDataString(organization);
        var projSeg = Uri.EscapeDataString(projectIdOrName);
        var repoSeg = Uri.EscapeDataString(repositoryName);
        return $"https://dev.azure.com/{orgSeg}/{projSeg}/_git/{repoSeg}";
    }
}
