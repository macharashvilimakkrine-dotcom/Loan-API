using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoanApi.Api.Application;
using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LoanApi.Tests;

public sealed class LoanApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public LoanApiFactory()
    {
        connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LoanDbContext>();
            services.RemoveAll<DbContextOptions<LoanDbContext>>();
            services.AddDbContext<LoanDbContext>(options => options.UseSqlite(connection));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LoanDbContext>();
        SeedTestData(db);
        return host;
    }

    private static void SeedTestData(LoanDbContext db)
    {
        const string userPassword = "User12345!";
        var nino = CreateUser("Nino", "Beridze", "nino.user", "nino@example.com", userPassword);
        var mariam = CreateUser("Mariam", "Gelashvili", "mariam.user", "mariam@example.com", userPassword);
        var giorgi = CreateUser("Giorgi", "Kapanadze", "giorgi.user", "giorgi@example.com", userPassword);
        giorgi.IsBlocked = true;
        giorgi.BlockedUntil = new DateTime(2099, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        db.Accountants.Add(new Accountant
        {
            FirstName = "Default",
            LastName = "Accountant",
            Username = "accountant",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Accountant123!")
        });
        db.Users.AddRange(nino, mariam, giorgi);
        db.SaveChanges();

        db.Loans.AddRange(
            new Loan { UserId = nino.Id, LoanType = LoanType.AutoLoan, Amount = 12_000, Currency = "GEL", Period = 24 },
            new Loan { UserId = nino.Id, LoanType = LoanType.Installment, Amount = 850, Currency = "GEL", Period = 10, Status = LoanStatus.Rejected },
            new Loan { UserId = mariam.Id, LoanType = LoanType.FastLoan, Amount = 700, Currency = "USD", Period = 6, Status = LoanStatus.Approved },
            new Loan { UserId = mariam.Id, LoanType = LoanType.Installment, Amount = 2_400, Currency = "GEL", Period = 18 });
        db.SaveChanges();
    }

    private static User CreateUser(string firstName, string lastName, string username, string email, string password) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        Username = username,
        Age = 25,
        Email = email,
        MonthlyIncome = 2_000,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}

public sealed class ApiIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Swagger_document_is_available()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("LoanApi.Api", body);
        Assert.False(document.RootElement.GetProperty("paths").TryGetProperty("/", out _));
    }

    [Fact]
    public async Task Protected_endpoint_rejects_anonymous_request()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/loans");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registration_validation_returns_bad_request()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        var request = new RegisterRequest("", "", "x", 15, "invalid", -1, "short");

        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Registration_succeeds_and_duplicate_returns_conflict()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        var request = new RegisterRequest("Ana", "Testadze", "ana.test", 28, "ana@example.com", 2200, "Password123!");

        var created = await client.PostAsJsonAsync("/api/auth/register", request);
        var duplicate = await client.PostAsJsonAsync("/api/auth/register", request);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotEmpty((await created.Content.ReadFromJsonAsync<AuthResponse>())!.Token);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("already in use", await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task User_can_login_and_read_only_their_own_profile()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        var nino = await LoginAsync(client, "nino.user", "User12345!");
        var mariam = await LoginAsync(factory.CreateClient(), "mariam.user", "User12345!");
        Authorize(client, nino.Token);

        var ownResponse = await client.GetAsync($"/api/users/{nino.UserId}");
        var otherResponse = await client.GetAsync($"/api/users/{mariam.UserId}");

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherResponse.StatusCode);
    }

    [Fact]
    public async Task User_created_loan_is_processing_and_has_location_header()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "nino.user", "User12345!")).Token);

        var response = await client.PostAsJsonAsync("/api/loans", new CreateLoanRequest(LoanType.FastLoan, 300, "usd", 4));
        var loan = await response.Content.ReadFromJsonAsync<LoanResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.NotNull(loan);
        Assert.Equal(LoanStatus.Processing, loan.Status);
        Assert.Equal("USD", loan.Currency);
    }

    [Fact]
    public async Task Blocked_user_cannot_create_loan()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "giorgi.user", "User12345!")).Token);

        var response = await client.PostAsJsonAsync("/api/loans", new CreateLoanRequest(LoanType.FastLoan, 300, "GEL", 4));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Blocked users", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task User_role_cannot_access_accountant_endpoints()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "nino.user", "User12345!")).Token);

        var response = await client.GetAsync("/api/accountant/loans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Accountant_role_cannot_use_user_loan_endpoints()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "accountant", "Accountant123!")).Token);

        var response = await client.GetAsync("/api/loans");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_cannot_read_another_users_loan_by_id()
    {
        using var factory = new LoanApiFactory();
        using var accountantClient = factory.CreateClient();
        using var userClient = factory.CreateClient();
        Authorize(accountantClient, (await LoginAsync(accountantClient, "accountant", "Accountant123!")).Token);
        var nino = await LoginAsync(userClient, "nino.user", "User12345!");
        Authorize(userClient, nino.Token);
        var loans = await accountantClient.GetFromJsonAsync<List<LoanResponse>>("/api/accountant/loans", JsonOptions);
        var anotherUsersLoan = loans!.First(loan => loan.UserId != nino.UserId);

        var response = await userClient.GetAsync($"/api/loans/{anotherUsersLoan.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_role_cannot_read_audit_logs()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "nino.user", "User12345!")).Token);

        var response = await client.GetAsync("/api/accountant/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_api_action_is_saved_and_visible_to_accountant()
    {
        using var factory = new LoanApiFactory();
        using var userClient = factory.CreateClient();
        using var accountantClient = factory.CreateClient();
        var user = await LoginAsync(userClient, "nino.user", "User12345!");
        Authorize(userClient, user.Token);
        await userClient.GetAsync("/api/loans");
        Authorize(accountantClient, (await LoginAsync(accountantClient, "accountant", "Accountant123!")).Token);

        var logs = await accountantClient.GetFromJsonAsync<List<AuditLogResponse>>("/api/accountant/audit-logs");
        var action = logs!.First(log => log.UserId == user.UserId && log.Method == "GET" && log.Path == "/api/loans");

        Assert.Equal("nino.user", action.Username);
        Assert.Equal(200, action.StatusCode);
        Assert.True(action.DurationMilliseconds >= 0);
        Assert.NotEqual(default, action.TimestampUtc);
    }

    [Fact]
    public async Task Accountant_can_read_all_loans_and_change_status()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "accountant", "Accountant123!")).Token);
        var loans = await client.GetFromJsonAsync<List<LoanResponse>>("/api/accountant/loans", JsonOptions);
        var target = loans!.First(loan => loan.Status == LoanStatus.Processing && loan.LoanType == LoanType.AutoLoan);
        var update = new UpdateAccountantLoanRequest(target.LoanType, target.Amount, target.Currency, target.Period, LoanStatus.Approved);

        var updateResponse = await client.PutAsJsonAsync($"/api/accountant/loans/{target.Id}", update);
        var getResponse = await client.GetAsync($"/api/accountant/loans/{target.Id}");
        var changed = await getResponse.Content.ReadFromJsonAsync<LoanResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(LoanStatus.Approved, changed!.Status);
    }

    [Fact]
    public async Task User_cannot_change_a_non_processing_loan()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "nino.user", "User12345!")).Token);
        var loans = await client.GetFromJsonAsync<List<LoanResponse>>("/api/loans", JsonOptions);
        var rejected = Assert.Single(loans!, loan => loan.Status == LoanStatus.Rejected);
        var update = new UpdateLoanRequest(rejected.LoanType, rejected.Amount, rejected.Currency, rejected.Period);

        var response = await client.PutAsJsonAsync($"/api/loans/{rejected.Id}", update);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task User_can_delete_an_owned_processing_loan()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "nino.user", "User12345!")).Token);
        var createResponse = await client.PostAsJsonAsync("/api/loans", new CreateLoanRequest(LoanType.FastLoan, 350, "GEL", 3));
        var loan = await createResponse.Content.ReadFromJsonAsync<LoanResponse>(JsonOptions);

        var deleteResponse = await client.DeleteAsync($"/api/loans/{loan!.Id}");
        var getResponse = await client.GetAsync($"/api/loans/{loan.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Accountant_can_delete_any_loan()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "accountant", "Accountant123!")).Token);
        var loans = await client.GetFromJsonAsync<List<LoanResponse>>("/api/accountant/loans", JsonOptions);
        var approved = loans!.First(loan => loan.Status == LoanStatus.Approved);

        var deleteResponse = await client.DeleteAsync($"/api/accountant/loans/{approved.Id}");
        var getResponse = await client.GetAsync($"/api/accountant/loans/{approved.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Missing_resource_returns_safe_json_error()
    {
        using var factory = new LoanApiFactory();
        using var client = factory.CreateClient();
        Authorize(client, (await LoginAsync(client, "accountant", "Accountant123!")).Token);

        var response = await client.GetAsync("/api/accountant/loans/999999");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Loan was not found", body);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Accountant_can_block_and_unblock_user_over_http()
    {
        using var factory = new LoanApiFactory();
        using var accountantClient = factory.CreateClient();
        using var userClient = factory.CreateClient();
        Authorize(accountantClient, (await LoginAsync(accountantClient, "accountant", "Accountant123!")).Token);
        var user = await LoginAsync(userClient, "mariam.user", "User12345!");
        Authorize(userClient, user.Token);

        var blockResponse = await accountantClient.PutAsJsonAsync($"/api/users/{user.UserId}/block", new BlockUserRequest(null));
        var deniedResponse = await userClient.PostAsJsonAsync("/api/loans", new CreateLoanRequest(LoanType.FastLoan, 100, "GEL", 2));
        var unblockResponse = await accountantClient.PutAsync($"/api/users/{user.UserId}/unblock", null);
        var allowedResponse = await userClient.PostAsJsonAsync("/api/loans", new CreateLoanRequest(LoanType.FastLoan, 100, "GEL", 2));

        Assert.Equal(HttpStatusCode.NoContent, blockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unblockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, allowedResponse.StatusCode);
    }

    [Fact]
    public async Task Accountant_can_read_any_profile_and_missing_user_returns_not_found()
    {
        using var factory = new LoanApiFactory();
        using var accountantClient = factory.CreateClient();
        using var userClient = factory.CreateClient();
        Authorize(accountantClient, (await LoginAsync(accountantClient, "accountant", "Accountant123!")).Token);
        var user = await LoginAsync(userClient, "nino.user", "User12345!");

        var profileResponse = await accountantClient.GetAsync($"/api/users/{user.UserId}");
        var missingProfileResponse = await accountantClient.GetAsync("/api/users/999999");
        var missingBlockResponse = await accountantClient.PutAsJsonAsync("/api/users/999999/block", new BlockUserRequest(null));
        var missingUnblockResponse = await accountantClient.PutAsync("/api/users/999999/unblock", null);

        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingProfileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingBlockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingUnblockResponse.StatusCode);
    }

    [Fact]
    public async Task User_cannot_update_another_users_processing_loan()
    {
        using var factory = new LoanApiFactory();
        using var accountantClient = factory.CreateClient();
        using var userClient = factory.CreateClient();
        Authorize(accountantClient, (await LoginAsync(accountantClient, "accountant", "Accountant123!")).Token);
        var nino = await LoginAsync(userClient, "nino.user", "User12345!");
        Authorize(userClient, nino.Token);
        var loans = await accountantClient.GetFromJsonAsync<List<LoanResponse>>("/api/accountant/loans", JsonOptions);
        var someoneElsesLoan = loans!.First(loan => loan.UserId != nino.UserId && loan.Status == LoanStatus.Processing);
        var update = new UpdateLoanRequest(someoneElsesLoan.LoanType, someoneElsesLoan.Amount, someoneElsesLoan.Currency, someoneElsesLoan.Period);

        var response = await userClient.PutAsJsonAsync($"/api/loans/{someoneElsesLoan.Id}", update);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<AuthResponse> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
