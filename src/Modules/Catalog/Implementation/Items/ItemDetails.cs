using System.Diagnostics.CodeAnalysis;

namespace EquipmentLibrary.Catalog.Items;

internal sealed record ItemDetails(string Title, string Description)
{
    internal static bool TryCreate(string? title, string? description, [NotNullWhen(true)] out ItemDetails? details)
    {
        title = title?.Trim();
        description = description?.Trim();
        if (title is not { Length: > 0 and <= 160 } || title.Contains('\0') ||
            description is not { Length: > 0 and <= 2000 } || description.Contains('\0'))
        {
            details = null;
            return false;
        }

        details = new ItemDetails(title, description);
        return true;
    }

    internal const string ValidationMessage = "Supply a title of 1–160 characters and a description of 1–2000 characters.";
}
