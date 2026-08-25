namespace LoanApi.Api.Domain;

public sealed class Accountant
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}

public sealed class User
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Email { get; set; } = string.Empty;
    public decimal MonthlyIncome { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime? BlockedUntil { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public List<Loan> Loans { get; set; } = [];

    public bool IsBlockedAt(DateTime utcNow) =>
        IsBlocked && (BlockedUntil is null || BlockedUntil > utcNow);

    public bool ClearExpiredBlock(DateTime utcNow)
    {
        if (!IsBlocked || BlockedUntil is null || BlockedUntil > utcNow)
        {
            return false;
        }

        IsBlocked = false;
        BlockedUntil = null;
        return true;
    }
}

public sealed class Loan
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public LoanType LoanType { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int Period { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Processing;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Action { get; set; }
    public int StatusCode { get; set; }
    public long DurationMilliseconds { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime TimestampUtc { get; set; }
}
