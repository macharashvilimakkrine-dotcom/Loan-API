using LoanApi.Api.Application;
using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public async Task Login_rejects_unknown_username()
    {
        await using var db = new LoanDbContext(new DbContextOptionsBuilder<LoanDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "unit-test-key-that-is-longer-than-thirty-two-characters",
            ["Jwt:Issuer"] = "LoanApi",
            ["Jwt:Audience"] = "LoanApi.Client"
        }).Build();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            new AuthService(db, configuration).LoginAsync(new LoginRequest("missing", "Password123!")));
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

    [Fact]
    public async Task Block_validator_accepts_null_and_future_but_rejects_past_dates()
    {
        var validator = new BlockUserValidator();

        var indefinite = await validator.ValidateAsync(new BlockUserRequest(null));
        var future = await validator.ValidateAsync(new BlockUserRequest(DateTime.UtcNow.AddDays(1)));
        var past = await validator.ValidateAsync(new BlockUserRequest(DateTime.UtcNow.AddDays(-1)));

        Assert.True(indefinite.IsValid);
        Assert.True(future.IsValid);
        Assert.False(past.IsValid);
        Assert.Contains(past.Errors, x => x.ErrorMessage.Contains("მომავალი UTC თარიღი"));
    }

    [Fact]
    public async Task Registration_and_login_validators_cover_valid_and_invalid_requests()
    {
        var registerValidator = new RegisterValidator();
        var valid = await registerValidator.ValidateAsync(new RegisterRequest("Ana", "Test", "ana.test", 25, "ana@example.com", 1000, "Password123"));
        var invalid = await registerValidator.ValidateAsync(new RegisterRequest("", "", "x!", 10, "bad", -1, "weak"));
        var emptyLogin = await new LoginValidator().ValidateAsync(new LoginRequest("", ""));

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.True(invalid.Errors.Count >= 7);
        Assert.False(emptyLogin.IsValid);
        Assert.Equal(2, emptyLogin.Errors.Count);
    }

    [Fact]
    public async Task Update_validators_reject_invalid_currency_amount_period_and_enums()
    {
        var userResult = await new UpdateLoanValidator().ValidateAsync(
            new UpdateLoanRequest((LoanType)999, 0, "12", 361));
        var accountantResult = await new AccountantLoanValidator().ValidateAsync(
            new UpdateAccountantLoanRequest((LoanType)999, -1, "EURO", 0, (LoanStatus)999));

        Assert.False(userResult.IsValid);
        Assert.True(userResult.Errors.Count >= 4);
        Assert.False(accountantResult.IsValid);
        Assert.True(accountantResult.Errors.Count >= 5);
    }
}

public sealed class UserEntityTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsBlockedAt_covers_indefinite_active_expired_and_unblocked_states()
    {
        Assert.False(new User().IsBlockedAt(Now));
        Assert.True(new User { IsBlocked = true }.IsBlockedAt(Now));
        Assert.True(new User { IsBlocked = true, BlockedUntil = Now.AddMinutes(1) }.IsBlockedAt(Now));
        Assert.False(new User { IsBlocked = true, BlockedUntil = Now }.IsBlockedAt(Now));
    }

    [Fact]
    public void ClearExpiredBlock_only_changes_an_expired_temporary_block()
    {
        var unblocked = new User();
        var indefinite = new User { IsBlocked = true };
        var active = new User { IsBlocked = true, BlockedUntil = Now.AddMinutes(1) };
        var expired = new User { IsBlocked = true, BlockedUntil = Now.AddMinutes(-1) };

        Assert.False(unblocked.ClearExpiredBlock(Now));
        Assert.False(indefinite.ClearExpiredBlock(Now));
        Assert.False(active.ClearExpiredBlock(Now));
        Assert.True(expired.ClearExpiredBlock(Now));
        Assert.False(expired.IsBlocked);
        Assert.Null(expired.BlockedUntil);
    }
}

public sealed class MiddlewareTests
{
    [Fact]
    public async Task Exception_middleware_maps_every_known_error_and_hides_unexpected_details()
    {
        var cases = new (Exception Exception, int StatusCode, string Message)[]
        {
            (new NotFoundException("missing"), 404, "missing"),
            (new ConflictException("conflict"), 409, "conflict"),
            (new ForbiddenException("forbidden"), 403, "forbidden"),
            (new UnauthorizedException("unauthorized"), 401, "unauthorized"),
            (new InvalidOperationException("private details"), 500, "სერვერზე მოხდა გაუთვალისწინებელი შიდა შეცდომა.")
        };

        foreach (var item in cases)
        {
            var context = new DefaultHttpContext();
            context.Response.Body = new MemoryStream();
            var middleware = new ExceptionHandlingMiddleware(
                _ => throw item.Exception,
                NullLogger<ExceptionHandlingMiddleware>.Instance);

            await middleware.InvokeAsync(context);
            context.Response.Body.Position = 0;
            string body = await new StreamReader(context.Response.Body).ReadToEndAsync();

            Assert.Equal(item.StatusCode, context.Response.StatusCode);
            Assert.Contains(item.Message, body);
            if (item.StatusCode == 500)
            {
                Assert.DoesNotContain("private details", body);
            }
        }
    }

    [Fact]
    public async Task Audit_log_storage_failure_does_not_break_the_request()
    {
        var db = new LoanDbContext(new DbContextOptionsBuilder<LoanDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await db.DisposeAsync();
        using var provider = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        var middleware = new AuditLoggingMiddleware(
            _ => Task.CompletedTask,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AuditLoggingMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
    }
}

public sealed class JwtConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_key_is_rejected(string? key)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = key })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => JwtConfiguration.GetSigningKey(configuration));

        Assert.Contains("outside source control", exception.Message);
    }

    [Fact]
    public void Short_key_is_rejected_and_long_key_is_returned()
    {
        var shortConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "too-short" })
            .Build();
        const string validKey = "a-valid-signing-key-with-at-least-32-characters";
        var validConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = validKey })
            .Build();

        Assert.Throws<InvalidOperationException>(() => JwtConfiguration.GetSigningKey(shortConfiguration));
        Assert.Equal(validKey, JwtConfiguration.GetSigningKey(validConfiguration));
    }
}
