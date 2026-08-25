using LoanApi.Api.Application;
using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LoanApi.Tests;

public sealed class LoanServiceTests
{
    private static LoanDbContext CreateDb() => new(new DbContextOptionsBuilder<LoanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task CreateAsync_starts_loan_in_processing_status()
    {
        await using var db = CreateDb();
        db.Users.Add(new User { Id = 1, Username = "m", Email = "m@test.local" });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);

        var result = await service.CreateAsync(1, new CreateLoanRequest(LoanType.AutoLoan, 1000, "usd", 12));

        Assert.Equal(LoanStatus.Processing, result.Status);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public async Task CreateAsync_rejects_blocked_user()
    {
        await using var db = CreateDb();
        db.Users.Add(new User { Id = 1, Username = "m", Email = "m@test.local", IsBlocked = true });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(1, new CreateLoanRequest(LoanType.FastLoan, 100, "GEL", 3)));
    }

    [Fact]
    public async Task UpdateAsync_rejects_non_processing_loan()
    {
        await using var db = CreateDb();
        db.Loans.Add(new Loan { Id = 1, UserId = 1, Status = LoanStatus.Approved });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(1, new UpdateLoanRequest(LoanType.FastLoan, 100, "GEL", 3), 1));
    }

    [Fact]
    public async Task User_cannot_view_another_users_loan()
    {
        await using var db = CreateDb();
        db.Loans.Add(new Loan { Id = 1, UserId = 2 });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(1, 1));
    }

    [Fact]
    public async Task Accountant_can_update_loan_status()
    {
        await using var db = CreateDb();
        db.Loans.Add(new Loan { Id = 1, UserId = 2 });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);

        await service.UpdateByAccountantAsync(1, new UpdateAccountantLoanRequest(LoanType.AutoLoan, 5000, "GEL", 24, LoanStatus.Approved));

        Assert.Equal(LoanStatus.Approved, (await db.Loans.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task Accountant_can_delete_approved_loan()
    {
        await using var db = CreateDb();
        db.Loans.Add(new Loan { Id = 1, UserId = 2, Status = LoanStatus.Approved });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);

        await service.DeleteByAccountantAsync(1);

        Assert.Null(await db.Loans.FindAsync(1));
    }

    [Fact]
    public async Task User_can_update_and_delete_own_processing_loan()
    {
        await using var db = CreateDb();
        db.Loans.Add(new Loan { Id = 1, UserId = 1 });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);

        await service.UpdateAsync(1, new UpdateLoanRequest(LoanType.Installment, 200, "USD", 6), 1);
        await service.DeleteAsync(1, 1);

        Assert.Null(await db.Loans.FindAsync(1));
    }

    [Fact]
    public async Task GetMineAsync_returns_only_current_users_loans()
    {
        await using var db = CreateDb();
        db.Loans.AddRange(new Loan { UserId = 1 }, new Loan { UserId = 2 });
        await db.SaveChangesAsync();
        var service = new LoanService(db, TimeProvider.System);

        var result = await service.GetMineAsync(1);

        Assert.Single(result);
        Assert.Equal(1, result[0].UserId);
    }

    [Fact]
    public async Task Missing_loan_returns_not_found_error()
    {
        await using var db = CreateDb();
        var service = new LoanService(db, TimeProvider.System);

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteByAccountantAsync(100));
    }

    [Fact]
    public async Task Expired_block_is_cleared_when_user_creates_a_loan()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        db.Users.Add(new User { Id = 1, Username = "m", Email = "m@test.local", IsBlocked = true, BlockedUntil = now.UtcDateTime.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var service = new LoanService(db, new FixedTimeProvider(now));

        var result = await service.CreateAsync(1, new CreateLoanRequest(LoanType.FastLoan, 100, "GEL", 2));

        Assert.Equal(LoanStatus.Processing, result.Status);
        Assert.False((await db.Users.FindAsync(1))!.IsBlocked);
        Assert.Null((await db.Users.FindAsync(1))!.BlockedUntil);
    }

    [Fact]
    public async Task Create_for_missing_user_returns_not_found_error()
    {
        await using var db = CreateDb();
        var service = new LoanService(db, TimeProvider.System);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(999, new CreateLoanRequest(LoanType.FastLoan, 100, "GEL", 2)));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

public sealed class UserServiceTests
{
    [Fact]
    public async Task Accountant_can_block_and_unblock_user()
    {
        await using var db = new LoanDbContext(new DbContextOptionsBuilder<LoanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Users.Add(new User { Id = 1, Username = "user", Email = "user@test.local" });
        await db.SaveChangesAsync();
        var service = new UserService(db, TimeProvider.System);

        await service.BlockAsync(1, DateTime.UtcNow.AddDays(1));
        Assert.True((await db.Users.FindAsync(1))!.IsBlocked);
        await service.UnblockAsync(1);

        Assert.False((await db.Users.FindAsync(1))!.IsBlocked);
    }
}

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Accountant_login_uses_the_separate_accountants_table()
    {
        await using var db = new LoanDbContext(new DbContextOptionsBuilder<LoanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Accountants.Add(new Accountant
        {
            FirstName = "Default",
            LastName = "Accountant",
            Username = "accountant",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Accountant123!")
        });
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "development-only-key-change-this-to-a-long-secret-123456789",
            ["Jwt:Issuer"] = "LoanApi",
            ["Jwt:Audience"] = "LoanApi.Client"
        }).Build();
        var service = new AuthService(db, configuration);

        var response = await service.LoginAsync(new LoginRequest("accountant", "Accountant123!"));

        Assert.Equal("Accountant", response.Role);
        Assert.Single(await db.Accountants.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            service.LoginAsync(new LoginRequest("accountant", "wrong-password")));
    }

    [Fact]
    public async Task Register_and_login_return_jwt_without_plain_password_storage()
    {
        await using var db = new LoanDbContext(new DbContextOptionsBuilder<LoanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "development-only-key-change-this-to-a-long-secret-123456789",
            ["Jwt:Issuer"] = "LoanApi",
            ["Jwt:Audience"] = "LoanApi.Client"
        }).Build();
        var service = new AuthService(db, configuration);

        await service.RegisterAsync(new RegisterRequest("Test", "User", "testuser", 25, "test@example.com", 1000, "Password123!"));
        var response = await service.LoginAsync(new LoginRequest("testuser", "Password123!"));

        Assert.NotEmpty(response.Token);
        Assert.NotEqual("Password123!", (await db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task Register_rejects_duplicate_username()
    {
        await using var db = new LoanDbContext(new DbContextOptionsBuilder<LoanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "development-only-key-change-this-to-a-long-secret-123456789", ["Jwt:Issuer"] = "LoanApi", ["Jwt:Audience"] = "LoanApi.Client" }).Build();
        var service = new AuthService(db, configuration);
        var request = new RegisterRequest("Test", "User", "sameuser", 25, "one@example.com", 1000, "Password123!");

        await service.RegisterAsync(request);

        await Assert.ThrowsAsync<ConflictException>(() => service.RegisterAsync(request with { Email = "two@example.com" }));
    }

    [Fact]
    public async Task Login_rejects_wrong_password()
    {
        await using var db = new LoanDbContext(new DbContextOptionsBuilder<LoanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "development-only-key-change-this-to-a-long-secret-123456789", ["Jwt:Issuer"] = "LoanApi", ["Jwt:Audience"] = "LoanApi.Client" }).Build();
        var service = new AuthService(db, configuration);
        await service.RegisterAsync(new RegisterRequest("Test", "User", "testuser", 25, "test@example.com", 1000, "Password123!"));

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(new LoginRequest("testuser", "wrong-password")));
    }
}

public sealed class ValidationTests
{
    [Fact]
    public async Task Loan_validator_rejects_invalid_amount_and_period()
    {
        var result = await new LoanValidator().ValidateAsync(new CreateLoanRequest(LoanType.FastLoan, 0, "GEL", 0));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 2);
    }
}
