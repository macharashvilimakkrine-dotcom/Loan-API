using LoanApi.Api.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Api.Controllers;

[ApiController, Route("api/accountant/audit-logs"), Authorize(Roles = "Accountant")]
public sealed class AuditLogsController(IAuditLogService service) : ControllerBase
{
    /// <summary>Returns the 100 most recent API actions.</summary>
    /// <remarks>Request bodies and passwords are never stored in the audit log.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AuditLogResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IReadOnlyList<AuditLogResponse>> GetRecent() => await service.GetRecentAsync();
}
