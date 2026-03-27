using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Infrastructure.Services;

public sealed class GitHubService : IGitHubService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IHttpClientFactory httpClientFactory, ILogger<GitHubService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<GitHubValidateResponse> ValidateTokenAsync(string personalAccessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personalAccessToken))
            return new GitHubValidateResponse(false, "GitHub PAT is required.", null);

        using var client = CreateApiClient(personalAccessToken);
        using var response = await client.GetAsync("user", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GitHub token validation failed with status {StatusCode}", response.StatusCode);
            return new GitHubValidateResponse(false, "Invalid GitHub PAT or insufficient scopes (repo).", null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var login = doc.RootElement.TryGetProperty("login", out var loginEl) ? loginEl.GetString() : null;
        return new GitHubValidateResponse(true, null, login);
    }

    public async Task<GitHubCheckReposResponse> CheckRepositoriesExistAsync(
        string personalAccessToken,
        string owner,
        IReadOnlyList<string> repoNames,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personalAccessToken) || string.IsNullOrWhiteSpace(owner))
            return new GitHubCheckReposResponse(false, "Owner and PAT are required.", null);

        using var client = CreateApiClient(personalAccessToken);
        var results = new List<RepoExistenceDto>();
        foreach (var name in repoNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            results.Add(new RepoExistenceDto(name, response.IsSuccessStatusCode));
        }

        return new GitHubCheckReposResponse(true, null, results);
    }

    public async Task<(bool Success, string? Error)> CreatePrivateRepositoryAsync(
        string personalAccessToken,
        string owner,
        string authenticatedLogin,
        string repoName,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateApiClient(personalAccessToken);
        var body = JsonSerializer.Serialize(new { name = repoName, @private = true });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var path = string.Equals(owner, authenticatedLogin, StringComparison.OrdinalIgnoreCase)
            ? "user/repos"
            : $"orgs/{Uri.EscapeDataString(owner)}/repos";

        using var response = await client.PostAsync(path, content, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return (true, null);

        var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("GitHub create repository failed: {Status} {Body}", response.StatusCode, Truncate(detail, 500));
        return (false, $"GitHub could not create repository '{repoName}' for owner '{owner}'.");
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private HttpClient CreateApiClient(string personalAccessToken)
    {
        var client = _httpClientFactory.CreateClient("github");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RepoMigrationTool/1.0");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
