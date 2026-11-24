namespace EquipmentLibrary.Lending.Domain;

public sealed record MaintenanceResolution
{
    private MaintenanceResolution(string outcome, string copyState)
    {
        Outcome = outcome;
        CopyState = copyState;
    }

    public static MaintenanceResolution Repair { get; } = new("repaired", "available");
    public static MaintenanceResolution Withdraw { get; } = new("withdrawn", "retired");

    public string Outcome { get; }
    public string CopyState { get; }

    public bool Matches(string? recordedOutcome, string? recordedNote, string note) =>
        recordedOutcome == Outcome && string.Equals(recordedNote, note, StringComparison.Ordinal);
}
