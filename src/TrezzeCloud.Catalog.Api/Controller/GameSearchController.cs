using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrezzeCloud.Catalog.Infrastructure.Data;
using TrezzeCloud.Catalog.Infrastructure.Search;

namespace TrezzeCloud.Catalog.Api.Controllers;

[ApiController]
[Route("search")]
public sealed class GameSearchController : ControllerBase
{
    private readonly IGameSearchService _search;
    private readonly CatalogDbContext _db;

    public GameSearchController(IGameSearchService search, CatalogDbContext db)
    {
        _search = search;
        _db = db;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length > 200 || limit is < 1 or > 100)
            return BadRequest("Informe q (ate 200 caracteres) e limit entre 1 e 100.");

        if (!_search.IsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Busca Elasticsearch nao configurada.");

        try
        {
            var items = await _search.SearchAsync(q.Trim(), limit, cancellationToken);
            return Ok(items);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Motor de busca indisponivel.");
        }
    }

    // Reindexacao manual para o catalogo ja cadastrado antes de habilitar o Elastic.
    [Authorize(Roles = "Admin")]
    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex(CancellationToken cancellationToken)
    {
        if (!_search.IsEnabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Busca Elasticsearch nao configurada.");

        var indexed = 0;
        await foreach (var game in _db.Games.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            await _search.IndexAsync(game, cancellationToken);
            indexed++;
        }

        return Ok(new { indexed });
    }
}
