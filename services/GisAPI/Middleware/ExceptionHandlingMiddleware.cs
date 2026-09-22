using System.Net;
using System.Text.Json;
using FluentValidation;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Domain.Exceptions;

namespace GisAPI.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Crédit IA refusé (22/09/2026) : ni une erreur ni un simple 400. L'écran lit le
        // code (AI_CREDIT_DISABLED / AI_CREDIT_EXHAUSTED), affiche le message et redessine sa
        // barre avec le crédit joint — contrat { code, message, credit } en 403 ou 429.
        // Journalisé en Information : un crédit épuisé est un fonctionnement normal.
        if (exception is AiCreditException aiCredit)
        {
            _logger.LogInformation("Crédit IA refusé ({Code}) : {Message}", aiCredit.Code, aiCredit.Message);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = aiCredit.StatusCode;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = aiCredit.Code,
                message = aiCredit.Message,
                credit = aiCredit.Credit
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            return;
        }

        var (statusCode, message, errors) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                "Validation failed",
                validationEx.Errors.Select(e => new { e.PropertyName, e.ErrorMessage })
            ),
            NotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                notFoundEx.Message,
                (object?)null
            ),
            ForbiddenAccessException forbiddenEx => (
                HttpStatusCode.Forbidden,
                forbiddenEx.Message,
                (object?)null
            ),
            ConflictException conflictEx => (
                HttpStatusCode.Conflict,
                conflictEx.Message,
                (object?)null
            ),
            DomainException domainEx => (
                HttpStatusCode.BadRequest,
                domainEx.Message,
                (object?)null
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                "An unexpected error occurred",
                (object?)null
            )
        };

        _logger.LogError(exception, "Exception occurred: {Message}", exception.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            status = statusCode,
            message,
            errors
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
