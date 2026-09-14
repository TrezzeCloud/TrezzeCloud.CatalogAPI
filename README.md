# TrezzeCloud.CatalogAPI

Microsserviço .NET 10 / ASP.NET Core responsável pelo catálogo de jogos, avaliações, biblioteca do usuário e início do fluxo de compra.

## Persistência e mensageria

- **SQL Server / Entity Framework Core:** jogos e biblioteca do usuário. As migrations são aplicadas automaticamente na inicialização.
- **MongoDB:** avaliações na coleção `gameReviews`, no banco definido por `MongoDb__DatabaseName`. Os documentos contêm `Id` (ObjectId), `GameId`, `UserId`, `Rating`, `Comment`, `CreatedAt` e `UpdatedAt`. A consulta filtra pelo jogo e ordena pelas avaliações mais recentes.
- **Redis:** cache de `GET /api/games`, com TTL absoluto de 5 minutos, chave lógica `games:all:v2` e prefixo `TrezzeCloud:`. O payload usa `GameCacheDto`, preservando ID, estado ativo, datas e demais campos sem reconstruir a entidade `Game`. Entradas antigas em `games:all` deixam de ser lidas e expiram pelo TTL.
- **Invalidação:** Create, Update e Delete removem a chave da listagem depois de `SaveChangesAsync`. Delete é lógico (`IsActive = false`); a listagem mantém os jogos desativados com esse estado.
- **RabbitMQ / MassTransit:** publica `OrderPlacedEvent` ao iniciar uma compra e consome `PaymentProcessedEvent` para adicionar jogos à biblioteca após aprovação, sem duplicação. A fila configurada é `catalog-payment-processed`.

Redis e MongoDB precisam estar acessíveis para suas operações. Falhas de cache são propagadas; uma falha ao invalidar pode ocorrer depois de a alteração já ter sido persistida no SQL Server.

## Variáveis de ambiente

Configure as variáveis antes de iniciar a API. O separador `__` representa seções de configuração do .NET. Os valores abaixo são exemplos locais.

| Variável | Descrição / exemplo |
|---|---|
| `ConnectionStrings__CatalogDatabase` | SQL Server: `Server=localhost,1433;Database=TrezzeCloudCatalog;User Id=sa;Password=<senha>;TrustServerCertificate=True` |
| `MongoDb__ConnectionString` | MongoDB: `mongodb://localhost:27017` |
| `MongoDb__DatabaseName` | Banco de avaliações, por exemplo `TrezzeCloudCatalog` |
| `Redis__ConnectionString` | Redis: `localhost:6379` |
| `RabbitMq__Host` | Host RabbitMQ, por exemplo `localhost` |
| `RabbitMq__Username` | Usuário RabbitMQ |
| `RabbitMq__Password` | Senha RabbitMQ |
| `Jwt__Issuer` | Emissor do JWT; padrão `TrezzeCloud` |
| `Jwt__Audience` | Destinatário do JWT; padrão `TrezzeCloud` |
| `Jwt__SecretKey` | Chave de assinatura compatível com a UsersAPI |
| `ASPNETCORE_ENVIRONMENT` | Ambiente, por exemplo `Development` |
| `ASPNETCORE_URLS` | Endereços de escuta quando executado sem launch profile |

Redis e MongoDB são registrados em `AddInfrastructure`; forneça suas configurações por variáveis ou configuração local. Não versione segredos.

## Endpoints

Envie `Authorization: Bearer <token>` nos endpoints autenticados. As operações de jogos que alteram dados exigem a role `Admin`. A identificação do usuário é lida de `NameIdentifier` no JWT.

| Método | Rota | Autorização | Comportamento |
|---|---|---|---|
| GET | `/api/games` | Pública | Lista jogos; consulta/preenche Redis; 200 |
| GET | `/api/games/{id}` | Pública | Consulta SQL; 200 ou 404 |
| POST | `/api/games` | Admin | Cria jogo e invalida cache; 201 |
| PUT | `/api/games/{id}` | Admin | Atualiza jogo e invalida cache; 204 ou 404 |
| DELETE | `/api/games/{id}` | Admin | Desativa jogo e invalida cache; 204 ou 404 |
| GET | `/api/games/{gameId}/reviews` | Pública | Consulta MongoDB; 200, inclusive lista vazia |
| POST | `/api/games/{gameId}/reviews` | JWT | Cria avaliação; 201; nota inválida retorna 400 |
| POST | `/api/store/purchase/{gameId}` | JWT | Inicia compra assíncrona; 202; 404 se inexistente; 400 se indisponível ou já adquirido |
| GET | `/api/store/my-library` | JWT | Consulta biblioteca do usuário; 200 |

Corpo para criar ou atualizar um jogo:

```json
{
  "title": "Cyber Drift",
  "description": "Arcade racing game",
  "price": 99.90,
  "category": "Racing",
  "imageUrl": "https://example.com/game.png",
  "disponibilizationDate": "2026-01-01T00:00:00Z"
}
```

Corpo para criar uma avaliação (nota inteira entre 1 e 5):

```json
{
  "rating": 5,
  "comment": "Ótimo jogo!"
}
```

O ID do usuário vem do token, não do corpo. Os endpoints de avaliações atualmente não verificam a existência do jogo no SQL Server. Não há endpoints HTTP de atualização ou exclusão de avaliações.

## Executar e validar localmente

Requer SDK .NET 10. Para executar a API, disponibilize SQL Server, MongoDB, Redis e RabbitMQ e configure as variáveis acima. Execute na raiz deste repositório:

```powershell
dotnet restore TrezzeCloud.CatalogAPI.slnx
dotnet build TrezzeCloud.CatalogAPI.slnx --no-restore
dotnet test TrezzeCloud.CatalogAPI.slnx --no-build --no-restore
dotnet run --project src/TrezzeCloud.Catalog.Api --launch-profile https
```

O perfil HTTPS atende em `https://localhost:7171` e HTTP em `http://localhost:5013`. A API usa redirecionamento HTTPS. Swagger UI: `/swagger`; especificação: `/swagger/v1/swagger.json`.

Os testes não exigem serviços externos: usam EF Core InMemory, cache distribuído em memória com a serialização JSON de `CacheService` e mocks do driver MongoDB e do repositório. Cobrem cache hit/miss, preservação de estado, invalidação após mutações, criação/consulta de avaliações, validação de nota e identidade, além dos fluxos existentes de compra e biblioteca. Não substituem testes de integração com serviços reais.

## Seed de biblioteca

O seed de `UserLibrary` usa um único usuário de teste com cinco jogos:

- ID: `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`
- Nome: `Usuario Teste`
- E-mail: `teste@trezzecloud.com`

Esse usuário deve existir na UsersAPI com o mesmo ID. Os dados são destinados ao desenvolvimento local.

## Docker e Kubernetes

```powershell
docker build -t trezzecloud-catalog-api .
```

Os manifestos Kubernetes estão centralizados no repositório `TrezzeCloud.Orchestration`, no caminho `k8s/catalog-api`, conforme [k8s/README.md](k8s/README.md).
