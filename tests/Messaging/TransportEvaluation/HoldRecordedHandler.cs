using Npgsql;

namespace EquipmentLibrary.TransportEvaluation;

public static class HoldRecordedHandler
{
    public static async Task Handle(HoldRecorded message, NpgsqlDataSource database, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand("""
            INSERT INTO pickup_desk.projected_holds(id) VALUES ($1) ON CONFLICT DO NOTHING
            """);
        command.Parameters.AddWithValue(message.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
