using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ProjectWork.Infrastructure.Persistence;
using ProjectWork.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace ProjectWork.PresentationTests;

[CollectionDefinition("HTTP API", DisableParallelization = true)]
public sealed class HttpApiCollection : ICollectionFixture<HttpApiFixture>;

public sealed class HttpApiFixture : IAsyncLifetime
{
    public const string Secret = "Http-Tests-Only-Secret-At-Least-32-Bytes";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("sprint8_http").WithUsername("postgres").WithPassword("postgres").Build();
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Minimal hosting может прочитать настройки до ConfigureAppConfiguration.
                // Заменяем уже зарегистрированный контекст и только значения JWT тестового окружения.
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.GetConnectionString()));
                services.RemoveAll<JwtOptions>();
                services.AddSingleton(new JwtOptions(Secret, "http-tests", "http-tests", 60));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
                    options.TokenValidationParameters.ValidIssuer = "http-tests";
                    options.TokenValidationParameters.ValidAudience = "http-tests";
                });
            });
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                    ["Jwt:Secret"] = Secret, ["Jwt:Issuer"] = "http-tests",
                    ["Jwt:Audience"] = "http-tests", ["Jwt:LifetimeMinutes"] = "60"
                }));
        });
        // Запускается настоящий конвейер API, включая миграции, JWT и авторизацию.
        using var client = CreateClient();
        var response = await client.GetAsync("/events");
        response.EnsureSuccessStatusCode();
    }

    public HttpClient CreateClient() => _factory!.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
    });

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
