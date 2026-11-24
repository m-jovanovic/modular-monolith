using EquipmentLibrary.Lending.Domain;

namespace EquipmentLibrary.Lending.Application.Loans.RenewLoan;

public sealed record RenewLoanCommand(Guid LoanId, Guid BorrowerId);

public abstract record RenewLoanResult
{
    private RenewLoanResult() { }

    public sealed record Renewed(Loan Loan) : RenewLoanResult;
    public sealed record Missing : RenewLoanResult;
    public sealed record Forbidden : RenewLoanResult;
    public sealed record NotRenewable : RenewLoanResult;
}

public interface ILoanRenewalStore
{
    // The decision runs against the locked current loan. A successful renewal is saved before releasing its lock.
    Task<RenewLoanResult> UpdateUnderLockAsync(
        Guid loanId, Func<Loan, RenewLoanResult> decide, CancellationToken cancellationToken);
}

public sealed class RenewLoanCommandHandler(ILoanRenewalStore loans, TimeProvider clock)
{
    public Task<RenewLoanResult> HandleAsync(RenewLoanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentOutOfRangeException.ThrowIfEqual(command.BorrowerId, Guid.Empty);
        return loans.UpdateUnderLockAsync(command.LoanId, Decide, cancellationToken);

        RenewLoanResult Decide(Loan loan)
        {
            if (loan.BorrowerId != command.BorrowerId)
            {
                return new RenewLoanResult.Forbidden();
            }

            return loan.RenewAt(clock.GetUtcNow()) is Loan renewed
                ? new RenewLoanResult.Renewed(renewed)
                : new RenewLoanResult.NotRenewable();
        }
    }
}
