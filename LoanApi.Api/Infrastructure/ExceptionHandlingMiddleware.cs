using System.Net;
using System.Text.Json;
using LoanApi.Api.Application;

namespace LoanApi.Api.Infrastructure;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API exception");
            var status = exception switch { NotFoundException => HttpStatusCode.NotFound, ConflictException => HttpStatusCode.Conflict, ForbiddenException => HttpStatusCode.Forbidden, UnauthorizedException => HttpStatusCode.Unauthorized, _ => HttpStatusCode.InternalServerError };
            context.Response.StatusCode = (int)status; context.Response.ContentType = "application/json";
            var response = new ApiErrorResponse(status == HttpStatusCode.InternalServerError ? "An unexpected error occurred." : exception.Message);
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }
}
