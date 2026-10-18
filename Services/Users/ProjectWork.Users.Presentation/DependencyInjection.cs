using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ProjectWork.Users.Infrastructure.Security;
using ProjectWork.Users.Application.DTO;
using ProjectWork.Users.Presentation.Swagger;

namespace ProjectWork.Users.Presentation;

/// <summary>Настройки HTTP-слоя: контроллеры, JSON, Problem Details и Swagger.</summary>
public static class DependencyInjection
{
    /// <summary>Подключить Presentation, сохранив автоматическую валидацию ApiController.</summary>
    public static IServiceCollection AddPresentationServices(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = JwtOptions.FromConfiguration(configuration);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true, ValidIssuer = jwt.Issuer,
                ValidateAudience = true, ValidAudience = jwt.Audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "name", RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var id = context.Principal?.FindFirst("sub")?.Value;
                    var role = context.Principal?.FindFirst("role")?.Value;
                    if (!Guid.TryParse(id, out var userId) || userId == Guid.Empty ||
                        role is not ("User" or "Admin"))
                        context.Fail("Некорректные claims пользователя.");
                    return Task.CompletedTask;
                }
            };
        });
        services.AddAuthorization();
        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
                Description = "Вставьте JWT из ответа POST /auth/login без префикса Bearer."
            });
            options.OperationFilter<AuthorizeOperationFilter>();
            var apiXmlFilename = $"{typeof(DependencyInjection).Assembly.GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, apiXmlFilename));
            var applicationXmlFilename = $"{typeof(TokenInfo).Assembly.GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, applicationXmlFilename));
        });
        return services;
    }
}
