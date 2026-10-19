using ProjectWork.Events.Presentation;
using ProjectWork.Events.Application;
using ProjectWork.Events.Infrastructure;
using ProjectWork.Events.Presentation.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddPresentationServices(builder.Configuration);

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
app.Run();

namespace ProjectWork.Events.Presentation
{
    /// <summary>Маркер сборки для интеграционных HTTP-тестов.</summary>
    public sealed class ApiAssemblyMarker { }
}
