# TrezzeCloud.CatalogAPI

Microsserviço .NET 10 / ASP.NET Core responsável pelo catálogo de jogos, avaliações, biblioteca do usuário e início do fluxo de compra.

## Persistência e mensageria

- **SQL Server / Entity Framework Core:** jogos e biblioteca do usuário. As migrations são aplicadas automaticamente na inicialização.
- **MongoDB:** avaliações na coleção `gameReviews`, no banco definido por `MongoDb__DatabaseName`. Os documentos contêm `Id` (ObjectId), `GameId`, `UserId`, `Rating`, `Comment`, `CreatedAt` e `UpdatedAt`. A consulta filtra pelo jogo e ordena pelas avaliações mais recentes.
- **Redis:** cache de `GET /api/games`, com TTL absoluto de 5 minutos, chave lógica `games:all:v2` e prefixo `TrezzeCloud:`. O payload usa `GameCacheDto`, preservando ID, estado ativo, datas e demais campos sem reconstruir a entidade `Game`. Entradas antigas em `games:all` deixam de ser lidas e expiram pelo TTL.
- **Invalidação:** Create, Update e Delete removem a chave da listagem depois de `SaveChangesAsync`. Delete é lógico (`IsActive = false`); a listagem mantém os jogos desativados com esse estado.
- **RabbitMQ / MassTransit:** publica `OrderPlacedEvent` ao iniciar uma compra e consome `PaymentProcessedEvent` para adicionar jogos à biblioteca após aprovação, sem duplicação. A fila configurada é `catalog-payment-processed`.

Redis e MongoDB precisam estar acessíveis para suas operações. Falhas de cache são propagadas; uma falha ao invalidar pode ocorrer depois de a alteração já ter sido persistida no SQL Server.

## Busca avançada (Elasticsearch / Elastic Cloud)

A busca usa o índice externo `fcg-games`, independente do SQL Server. O endpoint público `GET /search?q=cybr%20drft&limit=20` utiliza `multi_match`, `fuzziness: AUTO` e ordenação por `_score` decrescente (maior peso para título). Somente jogos ativos e já disponibilizados aparecem nos resultados.

Ao criar, editar ou desativar um jogo, a API atualiza seu documento no Elasticsearch após persistir no SQL Server e invalidar o Redis. Se o Elasticsearch estiver temporariamente indisponível, a operação principal permanece persistida e a falha é registrada. Uma reconciliação periódica reindexa o catálogo para recuperar alterações pendentes. A sincronização é eventual diante de falhas; a proposta de produção com alto volume deve evoluir para um outbox persistente.

O índice é criado automaticamente na primeira operação quando a integração está configurada, e `POST /search/reindex` (Admin) permite reindexar dados antigos. Uma integração desconfigurada não impede a API de inicializar: `GET /search` retorna **503**, sem usar SQL como substituto do mecanismo de busca. Se o Elasticsearch falhar durante a consulta, a resposta também será 503.

**Não adicione endpoint ou API key ao código, appsettings de produção ou YAML.** As configurações devem ser injetadas por variáveis de ambiente ou Secrets do Kubernetes. Quando nenhum endpoint ou chave é fornecido, a busca fica desligada; quando só um deles é preenchido, a aplicação informa configuração incompleta. O endpoint deve usar HTTPS. A compatibilidade prevista é com a API REST do Elasticsearch/Elastic Cloud.

## Teste real de Elasticsearch no GitHub Actions (sem custo Azure)

O workflow `.github/workflows/fase4-ci-cd.yml` contém a job `search_integration`, independente de login no Azure, que valida a API REST do **Elasticsearch 9.5.5** em um contêiner temporário do GitHub runner.

O teste `ElasticsearchLiveTests` verifica as funcionalidades realmente executadas no motor de busca: criação de índice, indexação de jogos, correspondência tolerante a erros de digitação (`cybr drft`), ordenação por relevância com título favorecido, atualização de jogo, exclusão lógica de jogo inativo e exclusão de jogos com disponibilidade futura. Ele utiliza o mesmo `ElasticsearchGameSearchService` da CatalogAPI.

Para manter o código de produção seguro, o cliente **continua exigindo URL HTTPS e API key**. Somente no ambiente isolado do teste o runner cria um proxy HTTPS localhost com certificado autossinado; a validação relaxada de certificado fica apenas no teste e **não é utilizada pela aplicação**. Elasticsearch de CI tem autenticação desativada apenas por estar isolado em loopback e ser descartado ao terminar a job. Nunca exponha essa configuração na rede pública.

A job de integração é executada na branch `feature/fase4-elasticsearch`, no PR correspondente e em `main` após a integração. A execução normal dos testes unitários usa mocks e permanece independente de containers.

**Validação realizada em 30/10/2026:** [GitHub Actions #41125449248](https://github.com/TrezzeCloud/TrezzeCloud.CatalogAPI/actions/runs/41125449248) aprovou a job `search_integration` (1 teste real, 0 falhas) e a job `validate` (testes .NET e build Docker). O teste inicial havia identificado a ausência de suporte a `Transfer-Encoding: chunked` no proxy exclusivamente de CI; o parser foi corrigido e a validação repetida foi aprovada. **Esta execução do CI valida o motor de busca em um contêiner temporário, não substituindo o teste em uma instância gerenciada.** O teste complementar com **Elastic Cloud Serverless gerenciado** foi concluído com sucesso em **30/10/2026** pela CatalogAPI executando no AKS `aks-fcg-canadateste`.

**Evidência do teste gerenciado:** `TrezzeCloud.Orchestration/infra/azure/test-search-integrated.ps1` autenticou o administrador, reindexou **12 jogos existentes**, criou um jogo, confirmou `Fuzzy Search` com erro de digitação, verificou a atualização do título e a exclusão lógica do resultado da busca. Resultado: `TESTE ELASTICSEARCH GERENCIADO APROVADO`. O jogo de teste permaneceu desativado no SQL Server. A validação estrutural separada confirmou os seis Deployments 1/1 Ready e o Secret do Elasticsearch presente no namespace `fcg-fase4`.

**Como reproduzir:** configure `Elasticsearch__Endpoint` e `Elasticsearch__ApiKey` exclusivamente via Secrets, conforme `TrezzeCloud.Orchestration/infra/azure/configure-catalog-search.ps1`, e execute o teste integrado no ambiente do seu próprio AKS. O resultado da assinatura de referência não comprova, por si só, a reprodução em outra assinatura/fork. Nunca registre a API Key no README, workflow ou logs.

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
| `Elasticsearch__Endpoint` | URL HTTPS do cluster Elastic Cloud, sem barra final |
| `Elasticsearch__ApiKey` | API key do Elasticsearch, mantida como Secret |
| `Elasticsearch__IndexName` | Nome do índice, padrão `fcg-games` |
| `Elasticsearch__ReindexIntervalMinutes` | Frequência da reconciliação, padrão 5 min |
| `ASPNETCORE_ENVIRONMENT` | Ambiente, por exemplo `Development` |
| `ASPNETCORE_URLS` | Endereços de escuta quando executado sem launch profile |

Redis e MongoDB são registrados em `AddInfrastructure`; forneça suas configurações por variáveis ou configuração local. Não versione segredos.

## Endpoints

Envie `Authorization: Bearer <token>` nos endpoints autenticados. As operações de jogos que alteram dados exigem a role `Admin`. A identificação do usuário é lida de `NameIdentifier` no JWT.

| Método | Rota | Autorização | Comportamento |
|---|---|---|---|
| GET | `/search?q=termo&limit=20` | Pública | Fuzzy search e relevância no Elastic; 400 se inválido, 503 se não configurado |
| POST | `/search/reindex` | Admin | Reindexa todos os jogos do banco SQL para o Elastic |
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

## Segurança e segredos no desenvolvimento

Nenhuma senha, JWT secret ou string de conexão real deve ser versionada em `appsettings*.json`. O projeto inclui `UserSecretsId` no `.csproj`, permitindo armazenar segredos em `dotnet user-secrets` quando `ASPNETCORE_ENVIRONMENT=Development`.

Configure em `src/TrezzeCloud.Catalog.Api` os valores de `ConnectionStrings:CatalogDatabase`, `MongoDb:ConnectionString`, `Redis:ConnectionString`, `Jwt:SecretKey`, `RabbitMq:Username` e `RabbitMq:Password` pelo **Gerenciar Segredos do Usuário** do Visual Studio ou `dotnet user-secrets set`. O `Jwt:SecretKey` deve ser o **mesmo da UsersAPI** para autenticação compartilhada. Os demais hosts, por exemplo `localhost`, podem permanecer nos arquivos sem senha.

Para listar as chaves locais (atenção: o comando mostra os valores):

```powershell
dotnet user-secrets list --project src/TrezzeCloud.Catalog.Api
```

Não compartilhe a saída desse comando. User Secrets é um mecanismo de desenvolvimento e **não criptografa** os segredos. No AKS, mantenha os Secrets Kubernetes já criados; esta alteração não os modifica.

O CI verifica com `scripts/check-appsettings-secrets.py` se os arquivos `appsettings*.json` contêm valores sensíveis e executa um scan Trivy informativo da imagem Docker. Chaves ou senhas já publicadas em commits antigos não são removidas do histórico; avalie sua rotação se tiverem sido utilizadas fora de ambientes de teste.

## Executar e validar localmente

Requer SDK .NET 10. Para executar a API, disponibilize SQL Server, MongoDB, Redis e RabbitMQ e configure as variáveis acima. Execute na raiz deste repositório:

```powershell
dotnet restore TrezzeCloud.CatalogAPI.slnx
dotnet build TrezzeCloud.CatalogAPI.slnx --no-restore
dotnet test TrezzeCloud.CatalogAPI.slnx --no-build --no-restore
dotnet run --project src/TrezzeCloud.Catalog.Api --launch-profile https
```

O perfil HTTPS atende em `https://localhost:7171` e HTTP em `http://localhost:5013`. A API usa redirecionamento HTTPS. Swagger UI: `/swagger`; especificação: `/swagger/v1/swagger.json`.

Os testes não exigem serviços externos: usam EF Core InMemory, cache distribuído em memória com a serialização JSON de `CacheService` e mocks do driver MongoDB e do repositório. Cobrem busca fuzzy no payload do Elastic, indexação de alterações, status quando não configurado, cache hit/miss, preservação de estado, invalidação após mutações, criação/consulta de avaliações, validação de nota e identidade, além dos fluxos existentes de compra e biblioteca. Não substituem testes de integração com serviços reais.

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
