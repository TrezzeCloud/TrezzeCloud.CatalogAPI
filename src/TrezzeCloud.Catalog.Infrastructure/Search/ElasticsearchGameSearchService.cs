using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TrezzeCloud.Catalog.Domain.Entities;

namespace TrezzeCloud.Catalog.Infrastructure.Search;

public sealed class ElasticsearchGameSearchService : IGameSearchService
{
    private readonly HttpClient _http;
    private readonly string _indexName;
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private bool _indexReady;

    public bool IsEnabled { get; }

    public ElasticsearchGameSearchService(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        var endpoint = configuration["Elasticsearch:Endpoint"];
        var apiKey = configuration["Elasticsearch:ApiKey"];
        _indexName = configuration["Elasticsearch:IndexName"] ?? "fcg-games";

        if (!System.Text.RegularExpressions.Regex.IsMatch(_indexName, "^[a-z][a-z0-9_-]*$"))
            throw new InvalidOperationException("Elasticsearch:IndexName deve conter apenas letras minusculas, numeros, '_' ou '-'.");

        if (string.IsNullOrWhiteSpace(endpoint) && string.IsNullOrWhiteSpace(apiKey))
        {
            IsEnabled = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configure Elasticsearch:Endpoint e Elasticsearch:ApiKey juntos.");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Elasticsearch:Endpoint deve ser uma URL HTTPS valida.");

        _http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
        _http.Timeout = TimeSpan.FromSeconds(20);
        IsEnabled = true;
    }

    public async Task IndexAsync(Game game, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return;

        await EnsureIndexAsync(cancellationToken);

        var document = new
        {
            id = game.Id,
            title = game.Title,
            description = game.Description,
            category = game.Category,
            price = game.Price,
            imageUrl = game.ImageUrl,
            disponibilizationDate = game.DisponibilizationDate,
            isActive = game.IsActive,
            createdAt = game.CreatedAt
        };

        using var response = await _http.PutAsJsonAsync(
            $"{_indexName}/_doc/{game.Id:D}?refresh=wait_for", document, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<GameSearchHit>> SearchAsync(
        string query, int limit, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Elasticsearch nao configurado.");

        await EnsureIndexAsync(cancellationToken);

        var request = new
        {
            size = limit,
            query = new
            {
                @bool = new
                {
                    must = new object[]
                    {
                        new
                        {
                            multi_match = new
                            {
                                query,
                                fields = new[] { "title^4", "category^2", "description" },
                                type = "best_fields",
                                fuzziness = "AUTO"
                            }
                        }
                    },
                    filter = new object[]
                    {
                        new { term = new { isActive = true } },
                        new { range = new { disponibilizationDate = new { lte = "now" } } }
                    }
                }
            },
            sort = new object[]
            {
                new Dictionary<string, object> { ["_score"] = new { order = "desc" } },
                new Dictionary<string, object> { ["createdAt"] = new { order = "desc" } }
            }
        };

        using var response = await _http.PostAsJsonAsync(
            $"{_indexName}/_search", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var json = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var results = new List<GameSearchHit>();
        foreach (var hit in json.RootElement.GetProperty("hits").GetProperty("hits").EnumerateArray())
        {
            var source = hit.GetProperty("_source");
            results.Add(new GameSearchHit(
                source.GetProperty("id").GetGuid(),
                source.GetProperty("title").GetString() ?? string.Empty,
                source.GetProperty("description").GetString() ?? string.Empty,
                source.GetProperty("category").GetString() ?? string.Empty,
                source.GetProperty("price").GetDecimal(),
                source.GetProperty("imageUrl").GetString() ?? string.Empty,
                source.GetProperty("disponibilizationDate").GetDateTime(),
                hit.GetProperty("_score").GetDouble()));
        }

        return results;
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexReady)
            return;

        await _indexGate.WaitAsync(cancellationToken);
        try
        {
            if (_indexReady)
                return;

            using var check = await _http.GetAsync(_indexName, cancellationToken);
            if (check.StatusCode == HttpStatusCode.NotFound)
            {
                var mapping = new
                {
                    mappings = new
                    {
                        properties = new
                        {
                            id = new { type = "keyword" },
                            title = new { type = "text" },
                            description = new { type = "text" },
                            category = new { type = "text" },
                            price = new { type = "double" },
                            imageUrl = new { type = "keyword", ignore_above = 2048 },
                            disponibilizationDate = new { type = "date" },
                            isActive = new { type = "boolean" },
                            createdAt = new { type = "date" }
                        }
                    }
                };
                using var create = await _http.PutAsJsonAsync(_indexName, mapping, cancellationToken);
                if (!create.IsSuccessStatusCode && create.StatusCode != HttpStatusCode.BadRequest)
                    await EnsureSuccessAsync(create, cancellationToken);

                // Um 400 so e aceitavel se outra instancia criou o indice simultaneamente.
                if (create.StatusCode == HttpStatusCode.BadRequest)
                {
                    using var confirm = await _http.GetAsync(_indexName, cancellationToken);
                    await EnsureSuccessAsync(confirm, cancellationToken);
                }
            }
            else
            {
                await EnsureSuccessAsync(check, cancellationToken);
            }

            _indexReady = true;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var details = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Elasticsearch retornou HTTP {(int)response.StatusCode}: {details[..Math.Min(details.Length, 300)]}",
            null, response.StatusCode);
    }
}
