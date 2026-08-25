using LoanApi.Api.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Api.Controllers;

[ApiController, Route("api/users")]
public sealed class UsersController(IUserService service) : ControllerBase
{
    /// <summary>Returns the current user's profile, or any profile for an Accountant.</summary>
    [HttpGet("{id:int}"), Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<UserResponse> Get(int id) => await service.GetAsync(id, User);

    /// <summary>Blocks a user from creating new loans.</summary>
    /// <remarks>An optional UTC date can be supplied in BlockedUntil to end the block.</remarks>
    [HttpPut("{id:int}/block"), Authorize(Roles = "Accountant")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Block(int id, BlockUserRequest request) { await service.BlockAsync(id, request.BlockedUntil); return NoContent(); }

    /// <summary>Removes the loan restriction from a user.</summary>
    [HttpPut("{id:int}/unblock"), Authorize(Roles = "Accountant")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unblock(int id) { await service.UnblockAsync(id); return NoContent(); }
}
