using System.Net;
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
            context.Response.StatusCode = (int)status;
            var response = new ApiErrorResponse(status == HttpStatusCode.InternalServerError ? "სერვერზე მოხდა გაუთვალისწინებელი შიდა შეცდომა." : exception.Message);
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
