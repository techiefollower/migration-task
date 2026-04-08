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

    public async Task<GitHubOwnerKindResponse> GetOwnerKindAsync(
        string personalAccessToken,
        string owner,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personalAccessToken) || string.IsNullOrWhiteSpace(owner))
            return new GitHubOwnerKindResponse("unknown", "PAT and owner are required.");

        var login = NormalizeGithubLogin(owner);
        using var client = CreateApiClient(personalAccessToken);
        // Prefer /orgs — works when the token can read the org (may 403 until SSO is authorized on the PAT).
        using var orgResp = await client
            .GetAsync($"orgs/{Uri.EscapeDataString(login)}", cancellationToken)
            .ConfigureAwait(false);
        if (orgResp.IsSuccessStatusCode)
            return new GitHubOwnerKindResponse("organization", null);

        // GitHub's GET /users/{login} returns BOTH users and organizations; use "type" (User vs Organization).
        using var userResp = await client
            .GetAsync($"users/{Uri.EscapeDataString(login)}", cancellationToken)
            .ConfigureAwait(false);
        if (userResp.IsSuccessStatusCode)
        {
            await using var stream = await userResp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var accountType = TryGetJsonString(doc.RootElement, "type");
            if (string.Equals(accountType, "Organization", StringComparison.OrdinalIgnoreCase))
                return new GitHubOwnerKindResponse("organization", null);
            if (string.Equals(accountType, "User", StringComparison.OrdinalIgnoreCase))
            {
                // Fine-grained PATs or API quirks can misclassify; trust org membership for this token.
                if (await IsLoginInAuthenticatedUserOrgsAsync(client, login, cancellationToken).ConfigureAwait(false))
                    return new GitHubOwnerKindResponse("organization", null);

                return new GitHubOwnerKindResponse(
                    "user",
                    "This login is a personal GitHub user, not an organization. gh ado2gh migrate-repo uses GitHub Enterprise Importer with --github-org, so migrations usually require a GitHub organization. Enter your org name here (create one on GitHub if needed).");
            }

            if (await IsLoginInAuthenticatedUserOrgsAsync(client, login, cancellationToken).ConfigureAwait(false))
                return new GitHubOwnerKindResponse("organization", null);

            return new GitHubOwnerKindResponse(
                "unknown",
                $"GitHub returned an unexpected account type '{accountType ?? "(missing)"}' for '{login}'.");
        }

        if (await IsLoginInAuthenticatedUserOrgsAsync(client, login, cancellationToken).ConfigureAwait(false))
            return new GitHubOwnerKindResponse("organization", null);

        if (orgResp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return new GitHubOwnerKindResponse(
                "unknown",
                "Could not read this organization with your PAT (HTTP 403). If the org uses SAML SSO, open GitHub → Settings → Applications → authorize this token for the organization, then try again.");
        }

        return new GitHubOwnerKindResponse(
            "unknown",
            "Could not find this login on GitHub with your PAT (check spelling, org membership, and token scopes).");
    }

    /// <summary>Trim and normalize lookalike hyphens so pasted org names match GitHub's slug.</summary>
    private static string NormalizeGithubLogin(string owner)
    {
        var s = owner.Trim();
        if (s.Length == 0)
            return s;
        Span<char> buffer = stackalloc char[s.Length];
        var j = 0;
        foreach (var c in s)
        {
            buffer[j++] = c is '\u2013' or '\u2014' or '\u2212' ? '-' : c;
        }

        return new string(buffer[..j]);
    }

    private static string? TryGetJsonString(JsonElement root, string propertyName)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
        }

        return null;
    }

    /// <summary>True if <paramref name="ownerLogin"/> matches an org the authenticated user belongs to (case-insensitive).</summary>
    private async Task<bool> IsLoginInAuthenticatedUserOrgsAsync(
        HttpClient client,
        string ownerLogin,
        CancellationToken cancellationToken)
    {
        for (var page = 1; page <= 20; page++)
        {
            using var resp = await client
                .GetAsync($"user/orgs?per_page=100&page={page}", cancellationToken)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("user/orgs page {Page} returned {Status}", page, resp.StatusCode);
                return false;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var orgLogin = TryGetJsonString(el, "login");
                if (string.Equals(orgLogin, ownerLogin, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (doc.RootElement.GetArrayLength() < 100)
                break;
        }

        return false;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private HttpClient CreateApiClient(string personalAccessToken)
    {
        var client = _httpClientFactory.CreateClient("github");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RepoMigrationTool/1.0");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", personalAccessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
