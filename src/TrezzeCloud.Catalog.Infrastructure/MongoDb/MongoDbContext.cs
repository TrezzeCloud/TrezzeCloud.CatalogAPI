using MongoDB.Driver;

namespace TrezzeCloud.Catalog.Infrastructure.MongoDb;

public class MongoDbContext
{
    public IMongoDatabase Database { get; }

    public MongoDbContext(MongoDbSettings settings)
        : this(new MongoClient(settings.ConnectionString).GetDatabase(settings.DatabaseName))
    {
    }

    public MongoDbContext(IMongoDatabase database)
    {
        Database = database;
    }
}
