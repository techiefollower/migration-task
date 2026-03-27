using Microsoft.AspNetCore.Mvc;
using RepoMigration.Core.Contracts;
using RepoMigration.Core.Dtos;

namespace RepoMigration.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MigrationsController : ControllerBase
{
    private readonly IMigrationService _migrations;

    public MigrationsController(IMigrationService migrations)
    {
        _migrations = migrations;
    }

    [HttpPost("queue")]
    public async Task<ActionResult<QueueMigrationsResponse>> Queue([FromBody] QueueMigrationsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _migrations.QueueAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MigrationListItemDto>>> List(CancellationToken cancellationToken)
    {
        var items = await _migrations.ListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MigrationsSummaryDto>> Summary(CancellationToken cancellationToken)
    {
        var summary = await _migrations.GetSummaryAsync(cancellationToken);
        return Ok(summary);
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, [FromBody] RetryMigrationRequest request, CancellationToken cancellationToken)
    {
        var ok = await _migrations.RetryAsync(id, request, cancellationToken);
        if (!ok)
            return NotFound();
        return NoContent();
    }
}
