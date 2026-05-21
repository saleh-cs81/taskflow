using System.Text.Json;
using TaskFlow.API.Localization;
using TaskFlow.Application.Common.Exceptions;

namespace TaskFlow.API.Middleware;

// Translates application exceptions into localized RFC 7807 ProblemDetails responses.
public class ExceptionHandlingMiddleware(
    RequestDelegate next,
    IMessageLocalizer localizer,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (status, key, errors) = ex switch
        {
            ValidationAppException v => (StatusCodes.Status400BadRequest, v.MessageKey, v.Errors),
            UnauthorizedAppException u => (StatusCodes.Status401Unauthorized, u.MessageKey, null),
            NotFoundAppException n => (StatusCodes.Status404NotFound, n.MessageKey, null),
            ConflictAppException c => (StatusCodes.Status409Conflict, c.MessageKey, null),
            _ => (StatusCodes.Status500InternalServerError, "error.server", (IDictionary<string, string[]>?)null)
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Unhandled exception");

        var problem = new
        {
            type = $"https://httpstatuses.io/{status}",
            title = localizer.Get(key),
            status,
            errors
        };

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));
    }
}
