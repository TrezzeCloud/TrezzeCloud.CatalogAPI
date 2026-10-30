using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TrezzeCloud.Catalog.Domain.Entities;
using TrezzeCloud.Catalog.Infrastructure.Search;

namespace TrezzeCloud.Catalog.UnitTests;

// Executado na job search_integration com Elasticsearch real, isolado no runner.
// O endpoint HTTPS aponta para proxy TLS local; não há assinatura nem Secrets Azure.
public sealed class ElasticsearchLiveTests
{
    [Fact]
    [Trait("Category", "SearchIntegration")]
    public async Task Elasticsearch_Real_Should_Fuzzy_Rank_Update_And_Filter_Games()
    {
        var endpoint = Environment.GetEnvironmentVariable("FCG_TEST_ELASTIC_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // O CI possui uma job separada que fornece esse valor. Os testes normais
            // continuam sem depender de container Elasticsearch.
            return;
        }

        var indexName = "fcg-ci-" + Guid.NewGuid().ToString("N");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elasticsearch:Endpoint"] = endpoint,
                ["Elasticsearch:ApiKey"] = "integration-test-only",
                ["Elasticsearch:IndexName"] = indexName
            })
            .Build();

        // Exclusivo do teste: o proxy HTTPS local usa certificado autoassinado.
        // NUNCA utilizar DangerousAcceptAnyServerCertificateValidator na API.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler);
        var search = new ElasticsearchGameSearchService(http, config);

        var leading = new Game("Cyber Drift", "High speed arcade race", 19.9m,
            "Racing", "https://example.invalid/cyber", DateTime.UtcNow.AddDays(-1));
        var secondary = new Game("Space Voyage", "Cyber Drift has exciting circuits",
            29.9m, "Strategy", "https://example.invalid/space", DateTime.UtcNow.AddDays(-1));
        var future = new Game("Cyber Future", "Upcoming racing adventure",
            49.9m, "Racing", "https://example.invalid/future", DateTime.UtcNow.AddDays(2));
        var inactive = new Game("Cyber Retired", "Cyber racing game",
            9.9m, "Racing", "https://example.invalid/retired", DateTime.UtcNow.AddDays(-5));
        inactive.Disable();

        await search.IndexAsync(leading);
        await search.IndexAsync(secondary);
        await search.IndexAsync(future);
        await search.IndexAsync(inactive);

        // O titulo pesa 4x: correspondencia no titulo deve superar a descricao.
        var exactResults = await search.SearchAsync("cyber drift", 10);
        exactResults.Should().NotBeEmpty();
        exactResults[0].Id.Should().Be(leading.Id, "o titulo recebe maior relevancia");
        exactResults.Should().NotContain(x => x.Id == future.Id || x.Id == inactive.Id);

        // Erros leves na digitacao nao impedem encontrar o jogo.
        var fuzzyResults = await search.SearchAsync("cybr drft", 10);
        fuzzyResults.Should().Contain(x => x.Id == leading.Id);
        fuzzyResults.Should().NotContain(x => x.Id == future.Id || x.Id == inactive.Id);

        // A indexacao precisa refletir a atualizacao de dados.
        leading.Update("Ocean Quest", "Underwater exploration game", 19.9m,
            "Adventure", "https://example.invalid/ocean", DateTime.UtcNow.AddDays(-1));
        await search.IndexAsync(leading);

        var updatedResults = await search.SearchAsync("ocean quest", 10);
        updatedResults.Should().Contain(x => x.Id == leading.Id && x.Title == "Ocean Quest");

        // Exclusao logica deve retirar o jogo da busca.
        leading.Disable();
        await search.IndexAsync(leading);
        var disabledResults = await search.SearchAsync("ocean quest", 10);
        disabledResults.Should().NotContain(x => x.Id == leading.Id);
    }
}
