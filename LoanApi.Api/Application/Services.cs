using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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

public sealed class AuthService : IAuthService
{
    private readonly LoanDbContext db;
    private readonly IConfiguration configuration;

    public AuthService(LoanDbContext db, IConfiguration configuration)
    {
        this.db = db;
        this.configuration = configuration;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        bool userExists = await db.Users.AnyAsync(x => x.Username == request.Username || x.Email == request.Email)
            || await db.Accountants.AnyAsync(x => x.Username == request.Username);
        if (userExists)
        {
            throw new ConflictException("Username or email is already in use.");
        }

        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Username = request.Username,
            Age = request.Age,
            Email = request.Email,
            MonthlyIncome = request.MonthlyIncome,
            PasswordHash = global::BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return CreateResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var accountant = await db.Accountants.SingleOrDefaultAsync(x => x.Username == request.Username);
        if (accountant is not null)
        {
            if (global::BCrypt.Net.BCrypt.Verify(request.Password, accountant.PasswordHash))
            {
                return CreateResponse(accountant.Id, accountant.Username, UserRole.Accountant);
            }

            throw new UnauthorizedException("Invalid username or password.");
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == request.Username);
        if (user is null || !global::BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("Invalid username or password.");
        }

        return CreateResponse(user.Id, user.Username, UserRole.User);
    }

    private AuthResponse CreateResponse(User user)
        => CreateResponse(user.Id, user.Username, UserRole.User);

    private AuthResponse CreateResponse(int id, string username, UserRole role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role.ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims, expires: DateTime.UtcNow.AddHours(2), signingCredentials: credentials);
        var tokenText = new JwtSecurityTokenHandler().WriteToken(token);
        return new AuthResponse(tokenText, id, username, role.ToString());
    }
}

public sealed class UserService : IUserService
{
    private readonly LoanDbContext db;
    private readonly TimeProvider timeProvider;

    public UserService(LoanDbContext db, TimeProvider timeProvider)
    {
        this.db = db;
        this.timeProvider = timeProvider;
    }

    public async Task<UserResponse> GetAsync(int id, ClaimsPrincipal principal)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        bool isAccountant = principal.IsInRole(nameof(UserRole.Accountant));
        bool isOwner = user.Id.ToString() == principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!isAccountant && !isOwner)
        {
            throw new ForbiddenException("You can only view your own profile.");
        }

        return Map(user, timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task BlockAsync(int id, DateTime? until)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        user.IsBlocked = true;
        user.BlockedUntil = until;
        await db.SaveChangesAsync();
    }

    public async Task UnblockAsync(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        user.IsBlocked = false;
        user.BlockedUntil = null;
        await db.SaveChangesAsync();
    }

    private static UserResponse Map(User user, DateTime utcNow)
    {
        bool isBlocked = user.IsBlockedAt(utcNow);
        return new UserResponse(user.Id, user.FirstName, user.LastName, user.Username, user.Age, user.Email, user.MonthlyIncome, isBlocked, user.BlockedUntil, UserRole.User.ToString());
    }
}

public sealed class LoanService : ILoanService
{
    private readonly LoanDbContext db;
    private readonly TimeProvider timeProvider;

    public LoanService(LoanDbContext db, TimeProvider timeProvider)
    {
        this.db = db;
        this.timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<LoanResponse>> GetMineAsync(int userId)
    {
        return await db.Loans
            .Where(x => x.UserId == userId)
            .Select(x => Map(x))
            .ToListAsync();
    }

    public async Task<LoanResponse> GetAsync(int id, int userId)
    {
        var loan = await Find(id);
        CheckOwner(loan, userId);
        return Map(loan);
    }

    public async Task<LoanResponse> CreateAsync(int userId, CreateLoanRequest request)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            throw new NotFoundException("User was not found.");
        }

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        if (user.IsBlockedAt(utcNow))
        {
            throw new ForbiddenException("Blocked users cannot request a loan.");
        }

        if (user.ClearExpiredBlock(utcNow))
        {
            await db.SaveChangesAsync();
        }

        var loan = new Loan
        {
            UserId = userId,
            LoanType = request.LoanType,
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Period = request.Period
        };

        db.Loans.Add(loan);
        await db.SaveChangesAsync();
        return Map(loan);
    }

    public async Task UpdateAsync(int id, UpdateLoanRequest request, int userId)
    {
        var loan = await Find(id);
        CheckOwner(loan, userId);
        CheckProcessing(loan);
        loan.LoanType = request.LoanType;
        loan.Amount = request.Amount;
        loan.Currency = request.Currency.ToUpperInvariant();
        loan.Period = request.Period;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var loan = await Find(id);
        CheckOwner(loan, userId);
        CheckProcessing(loan);
        db.Loans.Remove(loan);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<LoanResponse>> GetAllAsync()
    {
        return await db.Loans.Select(x => Map(x)).ToListAsync();
    }

    public async Task<LoanResponse> GetByAccountantAsync(int id)
    {
        return Map(await Find(id));
    }

    public async Task UpdateByAccountantAsync(int id, UpdateAccountantLoanRequest request)
    {
        var loan = await Find(id);
        loan.LoanType = request.LoanType;
        loan.Amount = request.Amount;
        loan.Currency = request.Currency.ToUpperInvariant();
        loan.Period = request.Period;
        loan.Status = request.Status;
        await db.SaveChangesAsync();
    }

    public async Task DeleteByAccountantAsync(int id)
    {
        var loan = await Find(id);
        db.Loans.Remove(loan);
        await db.SaveChangesAsync();
    }

    private async Task<Loan> Find(int id)
    {
        var loan = await db.Loans.FindAsync(id);
        if (loan is null)
        {
            throw new NotFoundException("Loan was not found.");
        }

        return loan;
    }

    private static void CheckOwner(Loan loan, int userId)
    {
        if (loan.UserId != userId)
        {
            throw new ForbiddenException("You can only manage your own loans.");
        }
    }

    private static void CheckProcessing(Loan loan)
    {
        if (loan.Status != LoanStatus.Processing)
        {
            throw new ConflictException("Only processing loans can be changed.");
        }
    }

    private static LoanResponse Map(Loan loan)
    {
        return new LoanResponse(loan.Id, loan.UserId, loan.LoanType, loan.Amount, loan.Currency, loan.Period, loan.Status, loan.CreatedAtUtc);
    }
}

public sealed class AuditLogService : IAuditLogService
{
    private readonly LoanDbContext db;

    public AuditLogService(LoanDbContext db)
    {
        this.db = db;
    }

    public async Task<IReadOnlyList<AuditLogResponse>> GetRecentAsync()
    {
        return await db.AuditLogs
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
}
