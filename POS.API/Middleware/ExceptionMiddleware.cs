using System.Net;
using System.Text.Json;
using POS.Domain.Exceptions;

namespace POS.API.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
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
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.NotFound, "NotFound", ex.Message);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation error: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.BadRequest, "ValidationError", ex.Message);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.Conflict, "ConcurrencyConflict", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await WriteErrorResponse(context, HttpStatusCode.InternalServerError, "InternalServerError",
                "An error occurred while processing your request");
        }
    }

    private static async Task WriteErrorResponse(
        HttpContext context,
        HttpStatusCode statusCode,
        string error,
        string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            error,
            message,
            statusCode = (int)statusCode
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}

public static class ExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionMiddleware>();
    }
}
