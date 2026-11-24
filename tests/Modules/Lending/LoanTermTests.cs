using EquipmentLibrary.Lending.Domain;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

public sealed class LoanTermTests
{
    [Fact]
    public void RenewalExtendsAnOpenLoanFromItsOriginalDeadlineOnce()
    {
        var checkout = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(2));
        var loan = new Loan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), checkout, Loan.DueAfter(checkout), 0, null);
        Assert.Equal(checkout.AddDays(14), loan.DueAt);

        Loan? renewed = loan.RenewAt(loan.DueAt.AddTicks(-1));
        Assert.NotNull(renewed);
        Assert.Equal(checkout.AddDays(28), renewed.DueAt);
        Assert.Equal(1, renewed.Renewals);
        Assert.Null(renewed.RenewAt(loan.DueAt));
        Assert.Null(loan.RenewAt(loan.DueAt));
        Assert.Null(loan.RenewAt(loan.DueAt.AddTicks(1)));
        Assert.Null((loan with { ReturnedAt = checkout.AddDays(1) }).RenewAt(checkout.AddDays(2)));
    }
}
