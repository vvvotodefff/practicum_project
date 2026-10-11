using System.Text.Json.Serialization;
using ProjectWork.Application.DTO;

namespace ProjectWork.Presentation;

/// <summary>Настройки HTTP-слоя: контроллеры, JSON, Problem Details и Swagger.</summary>
public static class DependencyInjection
{
    /// <summary>Подключить Presentation, сохранив автоматическую валидацию ApiController.</summary>
    public static IServiceCollection AddPresentationServices(this IServiceCollection services)
    {
        services.AddControllers().AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddSwaggerGen(options =>
        {
            var apiXmlFilename = $"{typeof(DependencyInjection).Assembly.GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, apiXmlFilename));
            var applicationXmlFilename = $"{typeof(EventInfo).Assembly.GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, applicationXmlFilename));
        });
        return services;
    }
}
