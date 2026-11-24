using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.Lending;

internal static class LendingErrors
{
    internal static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, type: $"urn:equipment-library:problem:{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });

    internal static IResult Invalid(string title) => Problem(400, "invalid-request", title);
    internal static IResult CopyNotFound() => Problem(404, "copy-not-found", "This copy does not exist.");
    internal static IResult Forbidden() => Problem(403, "forbidden", "Permission denied.");
}
