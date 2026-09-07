using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrezzeCloud.Catalog.Application.DTOs;
using TrezzeCloud.Catalog.Domain.Entities;
using TrezzeCloud.Catalog.Infrastructure.Cache;
using TrezzeCloud.Catalog.Infrastructure.Data;

namespace TrezzeCloud.Catalog.Api.Controllers;

[ApiController]
[Route("api/games")]
public sealed class GameController : ControllerBase
{
    // Versioned to avoid reading entries serialized with the domain entity.
    private const string GamesCacheKey = "games:all:v2";
    private readonly CatalogDbContext _context;
    private readonly ICacheService _cacheService;

    public GameController(CatalogDbContext context, ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var cachedGames = await _cacheService.GetAsync<List<GameCacheDto>>(GamesCacheKey, cancellationToken);

        if (cachedGames is not null)
        {
            return Ok(cachedGames);
        }

        var games = await _context.Games.AsNoTracking()
            .Select(game => new GameCacheDto(
                game.Id, game.Title, game.Description, game.Price, game.Category,
                game.ImageUrl, game.DisponibilizationDate, game.IsActive, game.CreatedAt))
            .ToListAsync(cancellationToken);

        await _cacheService.SetAsync(GamesCacheKey, games, TimeSpan.FromMinutes(5), cancellationToken);

        return Ok(games);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var game = await _context.Games
            .FirstOrDefaultAsync(x => x.Id == id);

        if (game is null)
            return NotFound();

        return Ok(new GameResponse(
            game.Id,
            game.Title,
            game.Description,
            game.Price,
            game.Category,
            game.ImageUrl,
            game.DisponibilizationDate,
            game.IsAvailable()));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateGameRequest request)
    {
        var game = new Game(
            request.Title,
            request.Description,
            request.Price,
            request.Category,
            request.ImageUrl,
            request.DisponibilizationDate);

        await _context.Games.AddAsync(game);

        await _context.SaveChangesAsync();

        await _cacheService.RemoveAsync(GamesCacheKey);

        return CreatedAtAction(
            nameof(GetById),
            new { id = game.Id },
            new GameResponse(
                game.Id,
                game.Title,
                game.Description,
                game.Price,
                game.Category,
                game.ImageUrl,
                game.DisponibilizationDate,
                game.IsAvailable()));
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateGameRequest request)
    {
        var game = await _context.Games
            .FirstOrDefaultAsync(x => x.Id == id);

        if (game is null)
            return NotFound();

        game.Update(
            request.Title,
            request.Description,
            request.Price,
            request.Category,
            request.ImageUrl,
            request.DisponibilizationDate);

        await _context.SaveChangesAsync();

        await _cacheService.RemoveAsync(GamesCacheKey);

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var game = await _context.Games
            .FirstOrDefaultAsync(x => x.Id == id);

        if (game is null)
            return NotFound();

        game.Disable();

        await _context.SaveChangesAsync();

        await _cacheService.RemoveAsync(GamesCacheKey);

        return NoContent();
    }
}
