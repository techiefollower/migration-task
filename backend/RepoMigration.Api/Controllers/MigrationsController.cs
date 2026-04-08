using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/migrations")]
public class MigrationsController : ControllerBase
{
    private readonly IMigrationOrchestrator _orchestrator;

    public MigrationsController(IMigrationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>Validates tokens, org, and repo names, then runs <c>gh ado2gh migrate-repo</c> for each repository (may take a long time).</summary>
    [HttpPost("execute")]
    public async Task<ActionResult<ExecuteMigrationsResponse>> Execute(
        [FromBody] ExecuteMigrationsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orchestrator.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
