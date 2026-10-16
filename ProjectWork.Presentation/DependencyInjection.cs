using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ProjectWork.Domain.Entities;
using ProjectWork.Infrastructure.Security;
using ProjectWork.Application.DTO;
using ProjectWork.Presentation.Swagger;

namespace ProjectWork.Presentation;

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
                        !Enum.TryParse<UserRole>(role, out var parsedRole) ||
                        !Enum.IsDefined(parsedRole))
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
            var applicationXmlFilename = $"{typeof(EventInfo).Assembly.GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, applicationXmlFilename));
        });
        return services;
    }
}
