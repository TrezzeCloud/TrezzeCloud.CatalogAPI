using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrezzeCloud.Catalog.Infrastructure.Cache;
using TrezzeCloud.Catalog.Infrastructure.Data;
using TrezzeCloud.Catalog.Infrastructure.MongoDb;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Interfaces;
using TrezzeCloud.Catalog.Infrastructure.MongoDb.Repositories;

namespace TrezzeCloud.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("CatalogDatabase"),
                sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(10, TimeSpan.FromSeconds(5), null);
                });
        });

        var redisConnectionString = configuration["Redis:ConnectionString"] ?? throw new InvalidOperationException("Redis ConnectionString não configurada.");

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "TrezzeCloud:";
        });

        var mongoSettings = new MongoDbSettings
        {
            ConnectionString =
            configuration["MongoDb:ConnectionString"]
            ?? throw new InvalidOperationException(
                "MongoDb ConnectionString não configurada."),

            DatabaseName =
            configuration["MongoDb:DatabaseName"]
            ?? throw new InvalidOperationException(
                "MongoDb DatabaseName não configurado.")
        };

        services.AddSingleton(mongoSettings);

        services.AddSingleton<MongoDbContext>();

        services.AddScoped<IGameReviewRepository, GameReviewRepository>();
        services.AddScoped<ICacheService, CacheService>();

        return services;
    }

    public static async Task MigrateDatabaseAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await dbContext.Database.MigrateAsync();
                break;
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}