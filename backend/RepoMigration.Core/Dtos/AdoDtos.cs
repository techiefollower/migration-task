namespace RepoMigration.Core.Dtos;

public record AdoProjectsRequest(string Organization, string PersonalAccessToken);

public record AdoProjectDto(string Id, string Name);

public record AdoProjectsResponse(bool Valid, string? Error, IReadOnlyList<AdoProjectDto>? Projects);

public record AdoRepositoriesRequest(string Organization, string ProjectIdOrName, string PersonalAccessToken);

public record AdoRepositoryDto(string Id, string Name, string RemoteUrl);

public record AdoRepositoriesResponse(bool Valid, string? Error, IReadOnlyList<AdoRepositoryDto>? Repositories);
