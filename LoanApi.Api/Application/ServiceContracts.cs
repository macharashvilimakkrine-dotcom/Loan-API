using System.Security.Claims;

namespace LoanApi.Api.Application;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
}

public interface IUserService
{
    Task<UserResponse> GetAsync(int id, ClaimsPrincipal principal);
    Task BlockAsync(int id, DateTime? until);
    Task UnblockAsync(int id);
}

public interface ILoanService
{
    Task<IReadOnlyList<LoanResponse>> GetMineAsync(int userId);
    Task<LoanResponse> GetAsync(int id, int userId);
    Task<LoanResponse> CreateAsync(int userId, CreateLoanRequest request);
    Task UpdateAsync(int id, UpdateLoanRequest request, int userId);
    Task DeleteAsync(int id, int userId);
    Task<IReadOnlyList<LoanResponse>> GetAllAsync();
    Task<LoanResponse> GetByAccountantAsync(int id);
    Task UpdateByAccountantAsync(int id, UpdateAccountantLoanRequest request);
    Task DeleteByAccountantAsync(int id);
}

public interface IAuditLogService
{
    Task<IReadOnlyList<AuditLogResponse>> GetRecentAsync();
}
