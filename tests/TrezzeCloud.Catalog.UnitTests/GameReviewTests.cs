using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using TrezzeCloud.Catalog.Api.Controller;
using TrezzeCloud.Catalog.Domain.Entities;
using TrezzeCloud.Catalog.Infrastructure.MongoDb;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Documents;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Interfaces;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Repositories;

namespace TrezzeCloud.Catalog.UnitTests;

public sealed class GameReviewTests
{
    [Fact]
    public async Task Create_Should_Insert_Mongo_Document_And_Return_Generated_Fields()
    {
        var collection = new Mock<IMongoCollection<GameReviewDocument>>();
        var review = new GameReview { GameId = Guid.NewGuid(), UserId = Guid.NewGuid(), Rating = 5, Comment = "Great" };
        var id = ObjectId.GenerateNewId().ToString();
        GameReviewDocument? inserted = null;
        collection.Setup(x => x.InsertOneAsync(It.IsAny<GameReviewDocument>(), It.IsAny<InsertOneOptions>(), default))
            .Callback<GameReviewDocument, InsertOneOptions, CancellationToken>((document, _, _) =>
            {
                document.Id = id; // Mongo driver assigns the ID during insertion.
                inserted = document;
            }).Returns(Task.CompletedTask);

        var result = await Repository(collection).CreateAsync(review);

        inserted.Should().NotBeNull();
        inserted!.GameId.Should().Be(review.GameId);
        inserted.UserId.Should().Be(review.UserId);
        inserted.Rating.Should().Be(5);
        inserted.Comment.Should().Be("Great");
        result.Id.Should().Be(id);
        result.CreatedAt.Should().Be(inserted.CreatedAt).And.NotBe(default);
        collection.Verify(x => x.InsertOneAsync(It.IsAny<GameReviewDocument>(), It.IsAny<InsertOneOptions>(), default), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_Should_Filter_By_Game_Sort_And_Map_Documents(bool empty)
    {
        var collection = new Mock<IMongoCollection<GameReviewDocument>>();
        var gameId = Guid.NewGuid();
        var document = new GameReviewDocument
        {
            Id = ObjectId.GenerateNewId().ToString(), GameId = gameId, UserId = Guid.NewGuid(),
            Rating = 4, Comment = "Good", CreatedAt = DateTime.UtcNow.AddDays(-1), UpdatedAt = DateTime.UtcNow
        };
        var cursor = new Mock<IAsyncCursor<GameReviewDocument>>();
        cursor.Setup(x => x.Current).Returns(empty ? [] : new[] { document });
        cursor.SetupSequence(x => x.MoveNextAsync(default)).ReturnsAsync(true).ReturnsAsync(false);
        FilterDefinition<GameReviewDocument>? filter = null;
        FindOptions<GameReviewDocument, GameReviewDocument>? options = null;
        collection.Setup(x => x.FindAsync(It.IsAny<FilterDefinition<GameReviewDocument>>(),
                It.IsAny<FindOptions<GameReviewDocument, GameReviewDocument>>(), default))
            .Callback<FilterDefinition<GameReviewDocument>, FindOptions<GameReviewDocument, GameReviewDocument>, CancellationToken>(
                (f, o, _) => { filter = f; options = o; }).ReturnsAsync(cursor.Object);

        var result = await Repository(collection).GetByGameIdAsync(gameId);

        var render = new RenderArgs<GameReviewDocument>(BsonSerializer.LookupSerializer<GameReviewDocument>(), BsonSerializer.SerializerRegistry);
        filter!.Render(render).Should().Equal(Builders<GameReviewDocument>.Filter.Eq(x => x.GameId, gameId).Render(render));
        options!.Sort.Render(render).Should().Equal(new BsonDocument("CreatedAt", -1));
        if (empty) result.Should().BeEmpty();
        else result.Single().Should().BeEquivalentTo(document);
        cursor.Verify(x => x.Dispose(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Controller_Should_Create_Review_For_Authenticated_User()
    {
        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var repository = new Mock<IGameReviewRepository>();
        repository.Setup(x => x.CreateAsync(It.Is<GameReview>(r => r.GameId == gameId && r.UserId == userId && r.Rating == 5 && r.Comment == "Great")))
            .ReturnsAsync((GameReview review) => review);
        var controller = Controller(repository.Object, userId.ToString());

        var result = (CreatedAtActionResult)await controller.Create(gameId, new(5, "Great"));

        result.ActionName.Should().Be(nameof(GameReviewsController.Get));
        result.RouteValues!["gameId"].Should().Be(gameId);
        ((GameReview)result.Value!).UserId.Should().Be(userId);
        repository.VerifyAll();
    }

    [Fact]
    public async Task Controller_Should_Return_Reviews_For_Requested_Game()
    {
        var gameId = Guid.NewGuid();
        var reviews = new List<GameReview> { new() { GameId = gameId, Rating = 4 } };
        var repository = new Mock<IGameReviewRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByGameIdAsync(gameId)).ReturnsAsync(reviews);
        var result = (OkObjectResult)await Controller(repository.Object).Get(gameId);
        result.Value.Should().BeSameAs(reviews);
        repository.VerifyAll();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Controller_Should_Reject_Invalid_Rating(int rating)
    {
        var repository = new Mock<IGameReviewRepository>(MockBehavior.Strict);
        (await Controller(repository.Object, Guid.NewGuid().ToString()).Create(Guid.NewGuid(), new(rating, "Invalid")))
            .Should().BeOfType<BadRequestObjectResult>();
        repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task Controller_Should_Reject_Missing_Or_Invalid_User(string? userId)
    {
        var repository = new Mock<IGameReviewRepository>(MockBehavior.Strict);
        (await Controller(repository.Object, userId).Create(Guid.NewGuid(), new(5, "Great")))
            .Should().BeOfType<UnauthorizedResult>();
        repository.VerifyNoOtherCalls();
    }

    private static GameReviewRepository Repository(Mock<IMongoCollection<GameReviewDocument>> collection)
    {
        var database = new Mock<IMongoDatabase>();
        database.Setup(x => x.GetCollection<GameReviewDocument>("gameReviews", null)).Returns(collection.Object);
        return new GameReviewRepository(new MongoDbContext(database.Object));
    }

    private static GameReviewsController Controller(IGameReviewRepository repository, string? userId = null) => new(repository)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(userId is null ? [] :
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Test"))
            }
        }
    };
}
