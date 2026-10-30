using Microsoft.EntityFrameworkCore;
using TrezzeCloud.Catalog.Infrastructure.Data;
using TrezzeCloud.Catalog.Infrastructure.Search;

namespace TrezzeCloud.Catalog.Api.Services;

// Reconciliacao periodica para recuperar indexacoes interrompidas pela indisponibilidade do Elastic.
public sealed class GameSearchReindexWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GameSearchReindexWorker> _logger;
    private readonly TimeSpan _interval;

    public GameSearchReindexWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<GameSearchReindexWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var configuredMinutes = configuration.GetValue("Elasticsearch:ReindexIntervalMinutes", 5);
        _interval = TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 1, 1440));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var search = scope.ServiceProvider.GetRequiredService<IGameSearchService>();
                if (search.IsEnabled)
                {
                    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                    var indexed = 0;

                    await foreach (var game in db.Games.AsNoTracking()
                        .AsAsyncEnumerable().WithCancellation(stoppingToken))
                    {
                        await search.IndexAsync(game, stoppingToken);
                        indexed++;
                    }

                    _logger.LogInformation("Indice Elasticsearch sincronizado. Jogos processados: {Count}", indexed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Falha na reconciliacao do Elasticsearch. Nova tentativa no proximo ciclo.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
