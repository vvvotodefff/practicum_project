using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using ProjectWork.Users.Domain.Exceptions;

namespace ProjectWork.Users.Presentation.Middleware
{
    /// <summary>Преобразует исключения в HTTP-ответы Problem Details.</summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        /// <summary>Создать обработчик ошибок HTTP-конвейера.</summary>
        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        /// <summary>Выполнить запрос и обработать ошибку, пока ответ ещё не отправлен.</summary>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Отмена клиентом не является ошибкой 500; ответ уже может быть недоступен.
                throw;
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var (statusCode, title) = exception switch
            {
                NotFoundException => (StatusCodes.Status404NotFound, "Ресурс не найден"),
                DomainValidationException => (StatusCodes.Status400BadRequest, "Ошибка валидации"),
                ValidationException => (StatusCodes.Status400BadRequest, "Ошибка валидации"),
                ArgumentException => (StatusCodes.Status400BadRequest, "Некорректный запрос"),
                _ => (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера")
            };

            if (statusCode == StatusCodes.Status500InternalServerError)
                _logger.LogError(exception, "Необработанное исключение: {Message}", exception.Message);
            else
                _logger.LogWarning(exception, "Запрос отклонён: {Message}", exception.Message);

            var problemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = statusCode == StatusCodes.Status500InternalServerError
                    ? "Произошла непредвиденная ошибка. Попробуйте позже."
                    : exception.Message
            };

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(problemDetails, options: null,
                contentType: "application/problem+json; charset=utf-8", cancellationToken: context.RequestAborted);
        }
    }
}
