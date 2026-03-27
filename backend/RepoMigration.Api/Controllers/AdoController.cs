using Microsoft.AspNetCore.Mvc;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdoController : ControllerBase
{
    private readonly IAdoDevOpsService _ado;

    public AdoController(IAdoDevOpsService ado)
    {
        _ado = ado;
    }

    [HttpPost("projects")]
    public async Task<ActionResult<AdoProjectsResponse>> GetProjects([FromBody] AdoProjectsRequest request, CancellationToken cancellationToken)
    {
        var result = await _ado.GetProjectsAsync(request.Organization, request.PersonalAccessToken, cancellationToken);
        return Ok(result);
    }

    [HttpPost("repositories")]
    public async Task<ActionResult<AdoRepositoriesResponse>> GetRepositories([FromBody] AdoRepositoriesRequest request, CancellationToken cancellationToken)
    {
        var result = await _ado.GetRepositoriesAsync(
            request.Organization,
            request.ProjectIdOrName,
            request.PersonalAccessToken,
            cancellationToken);
        return Ok(result);
    }
}
