namespace EquipmentLibrary.Lending.Domain;

public sealed record Loan(
    Guid Id, Guid CopyId, Guid BorrowerId, DateTimeOffset CheckedOutAt, DateTimeOffset DueAt, int Renewals, DateTimeOffset? ReturnedAt)
{
    private static readonly TimeSpan Term = TimeSpan.FromDays(14);

    public static DateTimeOffset DueAfter(DateTimeOffset start) => start + Term;

    public Loan? RenewAt(DateTimeOffset now) => ReturnedAt.HasValue || Renewals != 0 || now >= DueAt
        ? null
        : this with { DueAt = DueAfter(DueAt), Renewals = Renewals + 1 };
}
