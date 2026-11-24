using Npgsql;

namespace EquipmentLibrary.Lending.Loans;

internal sealed record LoanView(
    Guid LoanId, Guid CopyId, DateTimeOffset CheckedOutAt, DateTimeOffset DueAt, int Renewals, DateTimeOffset? ReturnedAt)
{
    internal static LoanView Read(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3), reader.GetInt16(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
}
