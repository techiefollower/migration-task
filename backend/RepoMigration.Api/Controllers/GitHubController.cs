using Microsoft.AspNetCore.Mvc;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GitHubController : ControllerBase
{
    private readonly IGitHubService _github;

    public GitHubController(IGitHubService github)
    {
        _github = github;
    }

    [HttpPost("validate")]
    public async Task<ActionResult<GitHubValidateResponse>> Validate([FromBody] GitHubValidateRequest request, CancellationToken cancellationToken)
    {
        var result = await _github.ValidateTokenAsync(request.PersonalAccessToken, cancellationToken);
        return Ok(result);
    }

    [HttpPost("check-repositories")]
    public async Task<ActionResult<GitHubCheckReposResponse>> CheckRepositories(
        [FromBody] GitHubCheckReposRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _github.CheckRepositoriesExistAsync(
            request.PersonalAccessToken,
            request.Owner,
            request.RepoNames,
            cancellationToken);
        return Ok(result);
    }
}
