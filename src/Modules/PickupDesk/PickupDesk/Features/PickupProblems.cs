using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class PickupProblems
{
    internal static IResult Missing() => Create(404, "pickup-not-found", "Pickup was not found.");

    internal static IResult Create(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, type: $"urn:equipment-library:problem:{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
