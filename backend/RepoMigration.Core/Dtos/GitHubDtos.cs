namespace RepoMigration.Core.Dtos;

public record GitHubValidateRequest(string PersonalAccessToken);

public record GitHubValidateResponse(bool Valid, string? Error, string? Login);

public record GitHubCheckReposRequest(string PersonalAccessToken, string Owner, IReadOnlyList<string> RepoNames);

public record RepoExistenceDto(string Name, bool Exists);

public record GitHubCheckReposResponse(bool Valid, string? Error, IReadOnlyList<RepoExistenceDto>? Results);
