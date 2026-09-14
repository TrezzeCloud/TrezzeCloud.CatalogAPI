using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TrezzeCloud.Catalog.Infrastructure.Cache;
using TrezzeCloud.Catalog.Infrastructure.Data;
using TrezzeCloud.Contracts.Events;

namespace TrezzeCloud.Catalog.Api.Controllers;

[ApiController]
[Route("api/store")]
public sealed class StoreController : ControllerBase
{
    private readonly CatalogDbContext _context;

    private readonly IPublishEndpoint _publishEndpoint;

    public StoreController(
        CatalogDbContext context,
        IPublishEndpoint publishEndpoint,
        ICacheService cacheService)
    {
        _context = context;
        _publishEndpoint = publishEndpoint;
    }

    [Authorize]
    [HttpPost("purchase/{gameId:guid}")]
    public async Task<IActionResult> Purchase(Guid gameId)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (userIdClaim is null)
            return Unauthorized();

        var userId = Guid.Parse(userIdClaim.Value);

        var game = await _context.Games
            .FirstOrDefaultAsync(x => x.Id == gameId);

        if (game is null)
            return NotFound();

        if (!game.IsAvailable())
            return BadRequest("Game is not available yet.");

        var alreadyOwned = await _context.UserLibraries
            .AnyAsync(x =>
                x.UserId == userId &&
                x.GameId == gameId);

        if (alreadyOwned)
            return BadRequest("Game already owned.");

        await _publishEndpoint.Publish(new OrderPlacedEvent(
            Guid.NewGuid(),
            userId,
            game.Id,
            game.Price,
            DateTime.UtcNow));

        return Accepted(new
        {
            Message = "Purchase processing started."
        });
    }

    [Authorize]
    [HttpGet("my-library")]
    public async Task<IActionResult> MyLibrary()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);

        if (userIdClaim is null)
            return Unauthorized();

        var userId = Guid.Parse(userIdClaim.Value);

        var games = await _context.UserLibraries
            .Where(x => x.UserId == userId)
            .Join(
                _context.Games,
                library => library.GameId,
                game => game.Id,
                (library, game) => new
                {
                    game.Id,
                    game.Title,
                    game.Description,
                    game.Price,
                    game.Category,
                    game.ImageUrl,
                    library.PurchasedAt
                })
            .ToListAsync();

        return Ok(games);
    }
}