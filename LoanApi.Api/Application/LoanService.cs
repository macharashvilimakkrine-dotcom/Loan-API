using LoanApi.Api.Domain;
using LoanApi.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LoanApi.Api.Application;

public sealed class LoanService(LoanDbContext db, TimeProvider timeProvider) : ILoanService
{
    public async Task<IReadOnlyList<LoanResponse>> GetMineAsync(int userId) =>
        await db.Loans.Where(x => x.UserId == userId).Select(x => Map(x)).ToListAsync();

    public async Task<LoanResponse> GetAsync(int id, int userId)
    {
        var loan = await FindAsync(id);
        CheckOwner(loan, userId);
        return Map(loan);
    }

    public async Task<LoanResponse> CreateAsync(int userId, CreateLoanRequest request)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
        {
            throw new NotFoundException("მოთხოვნილი მომხმარებელი ვერ მოიძებნა.");
        }

        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;
        if (user.IsBlockedAt(utcNow))
        {
            throw new ForbiddenException("დაბლოკილ მომხმარებელს სესხის მოთხოვნის უფლება არ აქვს.");
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
        var loan = await FindAsync(id);
        CheckOwner(loan, userId);
        CheckProcessing(loan);
        ApplyUpdate(loan, request.LoanType, request.Amount, request.Currency, request.Period);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var loan = await FindAsync(id);
        CheckOwner(loan, userId);
        CheckProcessing(loan);
        db.Loans.Remove(loan);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<LoanResponse>> GetAllAsync() =>
        await db.Loans.Select(x => Map(x)).ToListAsync();

    public async Task<LoanResponse> GetByAccountantAsync(int id) => Map(await FindAsync(id));

    public async Task UpdateByAccountantAsync(int id, UpdateAccountantLoanRequest request)
    {
        var loan = await FindAsync(id);
        ApplyUpdate(loan, request.LoanType, request.Amount, request.Currency, request.Period);
        loan.Status = request.Status;
        await db.SaveChangesAsync();
    }

    public async Task DeleteByAccountantAsync(int id)
    {
        db.Loans.Remove(await FindAsync(id));
        await db.SaveChangesAsync();
    }

    private async Task<Loan> FindAsync(int id)
    {
        var loan = await db.Loans.FindAsync(id);
        return loan ?? throw new NotFoundException("მოთხოვნილი სესხი ვერ მოიძებნა.");
    }

    private static void ApplyUpdate(Loan loan, LoanType loanType, decimal amount, string currency, int period)
    {
        loan.LoanType = loanType;
        loan.Amount = amount;
        loan.Currency = currency.ToUpperInvariant();
        loan.Period = period;
    }

    private static void CheckOwner(Loan loan, int userId)
    {
        if (loan.UserId != userId)
        {
            throw new ForbiddenException("მომხმარებელს მხოლოდ საკუთარი სესხების მართვა შეუძლია.");
        }
    }

    private static void CheckProcessing(Loan loan)
    {
        if (loan.Status != LoanStatus.Processing)
        {
            throw new ConflictException("შეცვლა შესაძლებელია მხოლოდ დამუშავების პროცესში მყოფი სესხისთვის.");
        }
    }

    private static LoanResponse Map(Loan loan) =>
        new(loan.Id, loan.UserId, loan.LoanType, loan.Amount, loan.Currency, loan.Period, loan.Status, loan.CreatedAtUtc);
}
