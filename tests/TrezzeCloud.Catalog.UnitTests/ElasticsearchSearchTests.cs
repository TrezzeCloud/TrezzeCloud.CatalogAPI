using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using TrezzeCloud.Catalog.Api.Controllers;
using TrezzeCloud.Catalog.Application.DTOs;
using TrezzeCloud.Catalog.Domain.Entities;
using TrezzeCloud.Catalog.Infrastructure.Cache;
using TrezzeCloud.Catalog.Infrastructure.Data;
using TrezzeCloud.Catalog.Infrastructure.Search;

namespace TrezzeCloud.Catalog.UnitTests;

public sealed class ElasticsearchSearchTests
{
    [Fact]
    public async Task Search_Uses_Fuzziness_Relevance_And_Availability_Filters()
    {
        string? payload = null;
        var gameId = Guid.NewGuid();
        var handler = new StubHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK);

            payload = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """
            {"hits":{"hits":[{"_score":9.5,"_source":{"id":"GAME_ID","title":"Cyber Drift","description":"Arcade racing","category":"Racing","price":89.9,"imageUrl":"image","disponibilizationDate":"2026-01-01T00:00:00Z"}}]}}
            """.Replace("GAME_ID", gameId.ToString()));
        });

        var sut = NewService(handler);
        var hits = await sut.SearchAsync("cybr drft", 15);

        hits.Should().ContainSingle().Which.Score.Should().Be(9.5);
        hits.Single().Id.Should().Be(gameId);
        payload.Should().Contain("\"fuzziness\":\"AUTO\"");
        payload.Should().Contain("title^4");
        payload.Should().Contain("\"isActive\":true");
        payload.Should().Contain("\"_score\"");
        payload.Should().Contain("\"size\":15");
    }

    [Fact]
    public async Task Index_Publishes_Game_State_After_Mutation()
    {
        string? body = null;
        string? path = null;
        var handler = new StubHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK);

            path = request.RequestUri!.PathAndQuery;
            body = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, "{\"result\":\"updated\"}");
        });

        var game = new Game("Cyber Drift", "Arcade racing", 99.90m, "Racing", "img", DateTime.UtcNow.AddDays(-1));
        game.Disable();

        await NewService(handler).IndexAsync(game);

        path.Should().Be($"/fcg-games/_doc/{game.Id:D}?refresh=wait_for");
        body.Should().Contain("\"title\":\"Cyber Drift\"");
        body.Should().Contain("\"isActive\":false");
    }

    [Fact]
    public async Task Create_Update_And_Disable_Synchronize_Search_Index()
    {
        await using var db = NewDb();
        var cache = new Mock<ICacheService>();
        var search = new Mock<IGameSearchService>();
        search.SetupGet(x => x.IsEnabled).Returns(true);
        var controller = new GameController(db, cache.Object, search.Object);
        var request = new CreateGameRequest("Game", "Description", 10m, "Action", "img", DateTime.UtcNow.AddDays(-1));

        (await controller.Create(request)).Should().BeOfType<CreatedAtActionResult>();
        var game = await db.Games.SingleAsync();
        (await controller.Update(game.Id,
            new UpdateGameRequest("Updated", "Description", 10m, "Action", "img", DateTime.UtcNow.AddDays(-1))))
            .Should().BeOfType<NoContentResult>();
        (await controller.Delete(game.Id)).Should().BeOfType<NoContentResult>();

        search.Verify(x => x.IndexAsync(It.IsAny<Game>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        game.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Search_Requires_Query_And_Returns_503_Without_Elastic()
    {
        await using var db = NewDb();
        var search = new Mock<IGameSearchService>();
        var controller = new GameSearchController(search.Object, db);

        (await controller.Search(null)).Should().BeOfType<BadRequestObjectResult>();
        (await controller.Search("game")).Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(503);
        search.Verify(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ElasticsearchGameSearchService NewService(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Elasticsearch:Endpoint"] = "https://elastic.example.test",
                ["Elasticsearch:ApiKey"] = "test-key",
                ["Elasticsearch:IndexName"] = "fcg-games"
            }).Build();
        return new ElasticsearchGameSearchService(new HttpClient(handler), configuration);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static CatalogDbContext NewDb() => new(
        new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => response(request);
    }
}
