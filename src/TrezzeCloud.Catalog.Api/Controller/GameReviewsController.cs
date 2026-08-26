using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TrezzeCloud.Catalog.Domain.Entities;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Interfaces;

namespace TrezzeCloud.Catalog.Api.Controller;

[ApiController]
[Route("api/games/{gameId:guid}/reviews")]
public class GameReviewsController : ControllerBase
{
    private readonly IGameReviewRepository _repository;

    public GameReviewsController(
        IGameReviewRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid gameId)
    {
        var reviews =
            await _repository.GetByGameIdAsync(gameId);

        return Ok(reviews);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(
        Guid gameId,
        CreateGameReviewRequest request)
    {
        if (request.Rating is < 1 or > 5)
            return BadRequest(
                "A avaliação deve ser entre 1 e 5.");

        var userIdClaim =
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var review = new GameReview
        {
            GameId = gameId,
            UserId = userId,
            Rating = request.Rating,
            Comment = request.Comment
        };

        var result =
            await _repository.CreateAsync(review);

        return CreatedAtAction(
            nameof(Get),
            new { gameId },
            result);
    }
}

public record CreateGameReviewRequest(
    int Rating,
    string Comment);
