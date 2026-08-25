using LoanApi.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LoanApi.Api.Application;

public sealed class AuditLogService(LoanDbContext db) : IAuditLogService
{
    public async Task<IReadOnlyList<AuditLogResponse>> GetRecentAsync() =>
        await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.TimestampUtc)
            .Take(100)
            .Select(x => new AuditLogResponse(
                x.Id,
                x.UserId,
                x.Username,
                x.Method,
                x.Path,
                x.Action,
                x.StatusCode,
                x.DurationMilliseconds,
                x.IpAddress,
                x.UserAgent,
                x.TimestampUtc))
            .ToListAsync();
}
