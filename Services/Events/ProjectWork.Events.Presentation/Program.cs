using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ProjectWork.Events.Application;
using ProjectWork.Events.Infrastructure;
using ProjectWork.Events.Presentation;
using ProjectWork.Events.Presentation.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddPresentationServices(builder.Configuration);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: builder.Configuration["Otlp:ServiceName"]!))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!)))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

await app.Services.MigrateDatabaseAsync();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStatusCodePages();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapPrometheusScrapingEndpoint();
app.Run();

namespace ProjectWork.Events.Presentation
{
    /// <summary>Маркер сборки для интеграционных HTTP-тестов.</summary>
    public sealed class ApiAssemblyMarker { }
}
