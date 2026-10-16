using ProjectWork.Presentation;
using ProjectWork.Application;
using ProjectWork.Infrastructure;
using ProjectWork.Presentation.Middleware;

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

/// <summary>Точка входа для интеграционных HTTP-тестов.</summary>
public partial class Program { }
