using Microsoft.OpenApi;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using TrezzeCloud.Catalog.Api.Configurations;
using TrezzeCloud.Catalog.Infrastructure;
using TrezzeCloud.Catalog.Infrastructure.Search;
using TrezzeCloud.Catalog.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TrezzeCloud Catalog API",
        Version = "v1"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "JWT Authentication",
        Description = "Informe o token JWT",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Id = "Bearer",
            Type = ReferenceType.SecurityScheme
        }
    };

    options.AddSecurityDefinition("Bearer", securityScheme);

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

builder.Services.AddMassTransitConfiguration(builder.Configuration);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpClient<IGameSearchService, ElasticsearchGameSearchService>();
builder.Services.AddHostedService<GameSearchReindexWorker>();

builder.Services.AddJwtConfiguration(builder.Configuration);

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultPolicy", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

var app = builder.Build();

app.UseHttpsRedirection();

app.UseCors("DefaultPolicy");

app.UseSwagger();

app.UseSwaggerUI();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapScalarApiReference();

await DependencyInjection.MigrateDatabaseAsync(app.Services);

app.Run();