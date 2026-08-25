using System.Security.Claims;
using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;

namespace LoanApi.Api.Application;

public sealed class UserService(LoanDbContext db, TimeProvider timeProvider) : IUserService
{
    public async Task<UserResponse> GetAsync(int id, ClaimsPrincipal principal)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            throw new NotFoundException("მოთხოვნილი მომხმარებელი ვერ მოიძებნა.");
        }

        bool isAccountant = principal.IsInRole(nameof(UserRole.Accountant));
        bool isOwner = user.Id.ToString() == principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!isAccountant && !isOwner)
        {
            throw new ForbiddenException("მომხმარებელს მხოლოდ საკუთარი პროფილის ნახვა შეუძლია.");
        }

        bool isBlocked = user.IsBlockedAt(timeProvider.GetUtcNow().UtcDateTime);
        return new UserResponse(user.Id, user.FirstName, user.LastName, user.Username, user.Age, user.Email, user.MonthlyIncome, isBlocked, user.BlockedUntil, UserRole.User.ToString());
    }

    public async Task BlockAsync(int id, DateTime? until)
    {
        var user = await FindAsync(id);
        user.IsBlocked = true;
        user.BlockedUntil = until;
        await db.SaveChangesAsync();
    }

    public async Task UnblockAsync(int id)
    {
        var user = await FindAsync(id);
        user.IsBlocked = false;
        user.BlockedUntil = null;
        await db.SaveChangesAsync();
    }

    private async Task<User> FindAsync(int id)
    {
        var user = await db.Users.FindAsync(id);
        return user ?? throw new NotFoundException("მოთხოვნილი მომხმარებელი ვერ მოიძებნა.");
    }
}
