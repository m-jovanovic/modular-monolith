namespace EquipmentLibrary.PickupDesk.Preparation;

internal sealed class StaffPreparation(Guid? assignedTo, DateTimeOffset? readyAt, string? issue)
{
    internal Guid? AssignedTo { get; private set; } = assignedTo;
    internal DateTimeOffset? ReadyAt { get; private set; } = readyAt;
    internal string? Issue { get; private set; } = issue;

    internal string? Claim(Guid staffId)
    {
        if (AssignedTo.HasValue && AssignedTo != staffId)
        {
            return "pickup-already-claimed";
        }

        AssignedTo = staffId;
        return null;
    }

    internal string? Release(Guid staffId)
    {
        if (AssignedTo != staffId)
        {
            return "pickup-not-assigned-to-you";
        }

        if (ReadyAt.HasValue || Issue is not null)
        {
            return "pickup-preparation-in-progress";
        }

        AssignedTo = null;
        return null;
    }
}
