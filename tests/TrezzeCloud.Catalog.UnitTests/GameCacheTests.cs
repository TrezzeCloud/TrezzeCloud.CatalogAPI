using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using TrezzeCloud.Catalog.Api.Controllers;
using TrezzeCloud.Catalog.Application.DTOs;
using TrezzeCloud.Catalog.Domain.Entities;
using TrezzeCloud.Catalog.Infrastructure.Cache;
using TrezzeCloud.Catalog.Infrastructure.Data;

namespace TrezzeCloud.Catalog.UnitTests;

public sealed class GameCacheTests
{
    private const string Key = "games:all:v2";

    [Fact]
    public async Task Cache_Miss_Should_Project_And_Cache_Games_With_Ttl_And_Token()
    {
        await using var db = CreateDb();
        var game = NewGame();
        game.Disable();
        db.Games.Add(game);
        await db.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var cache = new Mock<ICacheService>(MockBehavior.Strict);
        cache.Setup(x => x.GetAsync<List<GameCacheDto>>(Key, token)).ReturnsAsync((List<GameCacheDto>?)null);
        cache.Setup(x => x.SetAsync(Key, It.Is<List<GameCacheDto>>(items =>
            items.Count == 1 && items[0].Id == game.Id && !items[0].IsActive && items[0].CreatedAt == game.CreatedAt),
            TimeSpan.FromMinutes(5), token)).Returns(Task.CompletedTask);

        var result = await new GameController(db, cache.Object).GetAll(token);

        var items = result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<List<GameCacheDto>>().Subject;
        items.Single().Should().BeEquivalentTo(game);
        cache.VerifyAll();
    }

    [Fact]
    public async Task Cache_Hit_Should_Preserve_All_Fields_Through_Json_Without_Database()
    {
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache = new CacheService(distributed);
        var db = CreateDb();
        var game = NewGame();
        game.Disable();
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var controller = new GameController(db, cache);
        var miss = (OkObjectResult)await controller.GetAll(default);
        await db.DisposeAsync(); // A hit must not query the disposed context.

        var hit = (OkObjectResult)await controller.GetAll(default);

        hit.Value.Should().BeEquivalentTo(miss.Value);
        ((List<GameCacheDto>)hit.Value!).Single().Should().BeEquivalentTo(game);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Mutations_Should_Invalidate_After_Save_And_Refresh_List(string operation)
    {
        await using var db = CreateDb();
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var realCache = new CacheService(distributed);
        var game = NewGame();
        db.Games.Add(game);
        await db.SaveChangesAsync();
        var controller = new GameController(db, realCache);
        await controller.GetAll(default);
        var cache = new Mock<ICacheService>(MockBehavior.Strict);
        cache.Setup(x => x.RemoveAsync(Key, default)).Returns(async () =>
        {
            db.ChangeTracker.HasChanges().Should().BeFalse();
            if (operation == "create") (await db.Games.CountAsync()).Should().Be(2);
            if (operation == "update") (await db.Games.SingleAsync()).Title.Should().Be("Updated");
            if (operation == "delete") (await db.Games.SingleAsync()).IsActive.Should().BeFalse();
            await realCache.RemoveAsync(Key);
        });
        var writer = new GameController(db, cache.Object);

        var result = operation switch
        {
            "create" => await writer.Create(new CreateGameRequest("New", "Description", 20, "Action", "image", DateTime.UtcNow)),
            "update" => await writer.Update(game.Id, new UpdateGameRequest("Updated", "Description", 30, "Action", "image", DateTime.UtcNow)),
            _ => await writer.Delete(game.Id)
        };

        if (operation == "create") result.Should().BeOfType<CreatedAtActionResult>();
        else result.Should().BeOfType<NoContentResult>();
        cache.Verify(x => x.RemoveAsync(Key, default), Times.Once);
        (await realCache.GetAsync<List<GameCacheDto>>(Key)).Should().BeNull();
        var refreshed = (List<GameCacheDto>)((OkObjectResult)await controller.GetAll(default)).Value!;
        if (operation == "create") refreshed.Should().HaveCount(2);
        if (operation == "update") refreshed.Single().Title.Should().Be("Updated");
        if (operation == "delete") refreshed.Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_Game_Should_Not_Invalidate_Cache()
    {
        await using var db = CreateDb();
        var cache = new Mock<ICacheService>(MockBehavior.Strict);
        var controller = new GameController(db, cache.Object);
        (await controller.Update(Guid.NewGuid(), new UpdateGameRequest("Missing", "", 1, "", "", DateTime.UtcNow)))
            .Should().BeOfType<NotFoundResult>();
        (await controller.Delete(Guid.NewGuid())).Should().BeOfType<NotFoundResult>();
        cache.VerifyNoOtherCalls();
    }

    private static Game NewGame() => new("Game", "Description", 10, "Action", "image", DateTime.UtcNow.AddDays(-1));
    private static CatalogDbContext CreateDb() => new(new DbContextOptionsBuilder<CatalogDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
