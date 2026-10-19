using ProjectWork.Bookings.Presentation;
using ProjectWork.Bookings.Application;
using ProjectWork.Bookings.Infrastructure;
using ProjectWork.Bookings.Presentation.Middleware;

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

namespace ProjectWork.Bookings.Presentation
{
    /// <summary>Маркер сборки для интеграционных HTTP-тестов.</summary>
    public sealed class ApiAssemblyMarker { }
}
