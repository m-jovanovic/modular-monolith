namespace EquipmentLibrary.Catalog;

internal static class CatalogProblem
{
    internal static IResult Invalid(string detail) =>
        Create(StatusCodes.Status400BadRequest, "invalid-request", "Invalid request", detail);

    internal static IResult MissingItem() =>
        Create(StatusCodes.Status404NotFound, "catalog-item-not-found", "Catalog item not found");

    internal static IResult MissingCategory() =>
        Create(StatusCodes.Status404NotFound, "catalog-category-not-found", "Catalog category not found");

    internal static IResult DuplicateCategory() =>
        Create(StatusCodes.Status409Conflict, "category-name-in-use", "A category with that name already exists");

    private static IResult Create(int status, string code, string title, string? detail = null) =>
        Results.Problem(detail, statusCode: status, title: title, type: $"urn:equipment-library:problem:{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
