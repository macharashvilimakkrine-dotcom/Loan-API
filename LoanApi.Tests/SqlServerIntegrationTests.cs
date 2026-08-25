using BCrypt.Net;
using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LoanApi.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerDatabaseFixture>
{
    public const string Name = "SQL Server integration";
}

public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public string ConnectionString { get; }

    public SqlServerDatabaseFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<SqlServerDatabaseFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();
        ConnectionString = configuration.GetConnectionString("TestConnection")
            ?? throw new InvalidOperationException(
                "Configure ConnectionStrings:TestConnection for the dedicated SQL Server test database.");

        var builder = new SqlConnectionStringBuilder(ConnectionString);
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || !builder.InitialCatalog.EndsWith("_Tests", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The SQL Server test database name must end with '_Tests'.");
        }
    }

    public LoanDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LoanDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new LoanDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [AuditLogs]; DELETE FROM [Loans]; DELETE FROM [Users]; DELETE FROM [Accountants];");
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerIntegrationTests(SqlServerDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetAsync();
    public Task DisposeAsync() => database.ResetAsync();

    [Fact]
    public async Task Real_migrations_create_all_required_tables()
    {
        await using var db = database.CreateDbContext();

        var expectedMigrations = db.Database.GetMigrations().ToArray();
        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        int tableCount = await ExecuteScalarAsync<int>(db,
            "SELECT COUNT(*) FROM sys.tables WHERE name IN ('Users','Accountants','Loans','AuditLogs','__EFMigrationsHistory')");

        Assert.Equal(expectedMigrations, appliedMigrations);
        Assert.Equal(5, tableCount);
    }

    [Fact]
    public async Task SqlServer_schema_has_expected_types_defaults_indexes_and_relationships()
    {
        await using var db = database.CreateDbContext();

        int requiredColumns = await ExecuteScalarAsync<int>(db,
            """
            SELECT COUNT(*)
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE
                (t.name='Users' AND c.name='MonthlyIncome' AND ty.name='decimal' AND c.precision=18 AND c.scale=2)
                OR (t.name='Users' AND c.name='IsBlocked' AND ty.name='bit' AND c.is_nullable=0)
                OR (t.name='Loans' AND c.name='Amount' AND ty.name='decimal' AND c.precision=18 AND c.scale=2)
                OR (t.name='Loans' AND c.name='CreatedAtUtc' AND ty.name='datetime2' AND c.is_nullable=0)
            """);
        int defaultCount = await ExecuteScalarAsync<int>(db,
            """
            SELECT COUNT(*) FROM sys.default_constraints dc
            JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
            WHERE OBJECT_NAME(dc.parent_object_id)='Users' AND c.name='IsBlocked'
            """);
        int uniqueIndexCount = await ExecuteScalarAsync<int>(db,
            """
            SELECT COUNT(*) FROM sys.indexes
            WHERE is_unique=1 AND name IN ('IX_Users_Username','IX_Users_Email','IX_Accountants_Username')
            """);
        int checkCount = await ExecuteScalarAsync<int>(db,
            "SELECT COUNT(*) FROM sys.check_constraints WHERE name IN ('CK_Loans_Amount','CK_Loans_Period')");
        int cascadeForeignKeyCount = await ExecuteScalarAsync<int>(db,
            "SELECT COUNT(*) FROM sys.foreign_keys WHERE name='FK_Loans_Users_UserId' AND delete_referential_action_desc='CASCADE'");

        Assert.Equal(4, requiredColumns);
        Assert.Equal(1, defaultCount);
        Assert.Equal(3, uniqueIndexCount);
        Assert.Equal(2, checkCount);
        Assert.Equal(1, cascadeForeignKeyCount);
    }

    [Fact]
    public async Task User_and_hashed_password_round_trip_through_real_sql_server()
    {
        await using var db = database.CreateDbContext();
        const string password = "SqlServerTest123!";
        db.Users.Add(CreateUser("sql.user", "sql.user@example.com", BCrypt.Net.BCrypt.HashPassword(password)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.Users.SingleAsync(x => x.Username == "sql.user");

        Assert.False(stored.IsBlocked);
        Assert.NotEqual(password, stored.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(password, stored.PasswordHash));
    }

    [Fact]
    public async Task SqlServer_enforces_unique_usernames_and_emails()
    {
        await using var db = database.CreateDbContext();
        db.Users.Add(CreateUser("duplicate", "one@example.com", "hash"));
        await db.SaveChangesAsync();
        db.Users.Add(CreateUser("duplicate", "two@example.com", "hash"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(100, 0)]
    [InlineData(100, 361)]
    public async Task SqlServer_rejects_invalid_loan_amount_or_period(decimal amount, int period)
    {
        await using var db = database.CreateDbContext();
        var user = CreateUser($"loan.user.{period}", $"loan.user.{period}@example.com", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.Loans.Add(new Loan
        {
            UserId = user.Id,
            LoanType = LoanType.FastLoan,
            Amount = amount,
            Currency = "GEL",
            Period = period
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_user_cascades_to_loans_in_sql_server()
    {
        await using var db = database.CreateDbContext();
        var user = CreateUser("cascade.user", "cascade@example.com", "hash");
        user.Loans.Add(new Loan
        {
            LoanType = LoanType.AutoLoan,
            Amount = 20000,
            Currency = "GEL",
            Period = 48
        });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        int userId = user.Id;
        db.ChangeTracker.Clear();

        await db.Users.Where(x => x.Id == userId).ExecuteDeleteAsync();

        Assert.False(await db.Loans.AnyAsync(x => x.UserId == userId));
    }

    private static User CreateUser(string username, string email, string passwordHash) => new()
    {
        FirstName = "SQL",
        LastName = "Test",
        Username = username,
        Age = 30,
        Email = email,
        MonthlyIncome = 3000,
        PasswordHash = passwordHash
    };

    private static async Task<T> ExecuteScalarAsync<T>(LoanDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }
}
