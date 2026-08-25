using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace LoanApi.Api.Application;

public sealed class AuthService(LoanDbContext db, IConfiguration configuration) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        bool userExists = await db.Users.AnyAsync(x => x.Username == request.Username || x.Email == request.Email)
            || await db.Accountants.AnyAsync(x => x.Username == request.Username);
        if (userExists)
        {
            throw new ConflictException("მომხმარებლის სახელი ან ელფოსტა უკვე გამოყენებულია.");
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
        return CreateResponse(user.Id, user.Username, UserRole.User);
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

            throw new UnauthorizedException("მომხმარებლის სახელი ან პაროლი არასწორია.");
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.Username == request.Username);
        if (user is null || !global::BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedException("მომხმარებლის სახელი ან პაროლი არასწორია.");
        }

        return CreateResponse(user.Id, user.Username, UserRole.User);
    }

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
