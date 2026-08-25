using FluentValidation;
using LoanApi.Api.Domain;

namespace LoanApi.Api.Application;

public sealed class RegisterValidator : AbstractValidator<RegisterRequest>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Username).NotEmpty().Length(3, 50).Matches("^[a-zA-Z0-9._-]+$");
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(18, 100);
        RuleFor(x => x.MonthlyIncome).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Password)
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("პაროლი უნდა შეიცავდეს დიდ ასოს.")
            .Matches("[a-z]").WithMessage("პაროლი უნდა შეიცავდეს პატარა ასოს.")
            .Matches("[0-9]").WithMessage("პაროლი უნდა შეიცავდეს ციფრს.");
    }
}
public sealed class LoginValidator : AbstractValidator<LoginRequest>
{ public LoginValidator() { RuleFor(x => x.Username).NotEmpty(); RuleFor(x => x.Password).NotEmpty(); } }
public sealed class LoanValidator : AbstractValidator<CreateLoanRequest>
{ public LoanValidator() { RuleFor(x => x.LoanType).IsInEnum(); RuleFor(x => x.Amount).GreaterThan(0); RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[a-zA-Z]{3}$"); RuleFor(x => x.Period).InclusiveBetween(1, 360); } }
public sealed class UpdateLoanValidator : AbstractValidator<UpdateLoanRequest>
{ public UpdateLoanValidator() { RuleFor(x => x.LoanType).IsInEnum(); RuleFor(x => x.Amount).GreaterThan(0); RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[a-zA-Z]{3}$"); RuleFor(x => x.Period).InclusiveBetween(1, 360); } }
public sealed class AccountantLoanValidator : AbstractValidator<UpdateAccountantLoanRequest>
{ public AccountantLoanValidator() { RuleFor(x => x.LoanType).IsInEnum(); RuleFor(x => x.Status).IsInEnum(); RuleFor(x => x.Amount).GreaterThan(0); RuleFor(x => x.Currency).NotEmpty().Length(3).Matches("^[a-zA-Z]{3}$"); RuleFor(x => x.Period).InclusiveBetween(1, 360); } }
public sealed class BlockUserValidator : AbstractValidator<BlockUserRequest>
{ public BlockUserValidator() { RuleFor(x => x.BlockedUntil).Must(x => x is null || x > DateTime.UtcNow).WithMessage("BlockedUntil უნდა იყოს მომავალი UTC თარიღი."); } }
