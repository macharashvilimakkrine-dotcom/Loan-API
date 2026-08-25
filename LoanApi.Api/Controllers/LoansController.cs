using System.Security.Claims;
using LoanApi.Api.Application;
using LoanApi.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Api.Controllers;

[ApiController, Route("api/loans"), Authorize(Roles = nameof(UserRole.User))]
public sealed class LoansController(ILoanService service) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Returns all loans belonging to the logged-in User.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LoanResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IReadOnlyList<LoanResponse>> GetMine() => await service.GetMineAsync(CurrentUserId);

    /// <summary>Returns one loan if it belongs to the logged-in User.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(LoanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<LoanResponse> Get(int id) => await service.GetAsync(id, CurrentUserId);

    /// <summary>Creates a new loan application.</summary>
    /// <remarks>New loans always start with Processing. Blocked users cannot create loans.</remarks>
    [HttpPost]
    [ProducesResponseType(typeof(LoanResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateLoanRequest request)
    {
        var loan = await service.CreateAsync(CurrentUserId, request);
        return CreatedAtAction(nameof(Get), new { id = loan.Id }, loan);
    }

    /// <summary>Updates the logged-in User's loan while its status is Processing.</summary>
    /// <remarks>The User cannot change the loan status.</remarks>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, UpdateLoanRequest request) { await service.UpdateAsync(id, request, CurrentUserId); return NoContent(); }

    /// <summary>Deletes the logged-in User's loan while its status is Processing.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id) { await service.DeleteAsync(id, CurrentUserId); return NoContent(); }
}
