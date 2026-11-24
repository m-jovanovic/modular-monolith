namespace EquipmentLibrary.Catalog.Contracts;

public interface ICatalogLookup
{
    // A missing ID returns null. Implementations never expose a tracked Catalog entity.
    Task<CatalogItemDescription?> GetItemAsync(Guid itemId, CancellationToken cancellationToken);
}

public sealed record CatalogItemDescription(Guid Id, string Title, string Description);
