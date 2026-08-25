using System.Diagnostics;
using System.Security.Claims;
using LoanApi.Api.Domain;

namespace LoanApi.Api.Infrastructure;

public sealed class AuditLoggingMiddleware(
    RequestDelegate next,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<AuditLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        DateTime timestampUtc = timeProvider.GetUtcNow().UtcDateTime;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            await SaveAuditLogAsync(context, timestampUtc, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task SaveAuditLogAsync(HttpContext context, DateTime timestampUtc, long durationMilliseconds)
    {
        try
        {
            string? userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            int? userId = int.TryParse(userIdValue, out int parsedUserId) ? parsedUserId : null;
            string? username = context.User.FindFirstValue(ClaimTypes.Name);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LoanDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Username = username,
                Method = context.Request.Method,
                Path = context.Request.Path.Value ?? "/",
                Action = context.GetEndpoint()?.DisplayName,
                StatusCode = context.Response.StatusCode,
                DurationMilliseconds = durationMilliseconds,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                TimestampUtc = timestampUtc
            });
            await db.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save the API audit log for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
    }
}
