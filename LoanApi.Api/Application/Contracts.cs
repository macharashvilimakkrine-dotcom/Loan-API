using LoanApi.Api.Domain;

namespace LoanApi.Api.Application;

public record RegisterRequest(string FirstName, string LastName, string Username, int Age, string Email, decimal MonthlyIncome, string Password);
public record LoginRequest(string Username, string Password);
public record AuthResponse(string Token, int UserId, string Username, string Role);
public record ApiErrorResponse(string Message);
public record CreateLoanRequest(LoanType LoanType, decimal Amount, string Currency, int Period);
public record UpdateLoanRequest(LoanType LoanType, decimal Amount, string Currency, int Period);
public record UpdateAccountantLoanRequest(LoanType LoanType, decimal Amount, string Currency, int Period, LoanStatus Status);
public record BlockUserRequest(DateTime? BlockedUntil);
public record UserResponse(int Id, string FirstName, string LastName, string Username, int Age, string Email, decimal MonthlyIncome, bool IsBlocked, DateTime? BlockedUntil, string Role);
public record LoanResponse(int Id, int UserId, LoanType LoanType, decimal Amount, string Currency, int Period, LoanStatus Status, DateTime CreatedAtUtc);
public record AuditLogResponse(long Id, int? UserId, string? Username, string Method, string Path, string? Action, int StatusCode, long DurationMilliseconds, string? IpAddress, string? UserAgent, DateTime TimestampUtc);
