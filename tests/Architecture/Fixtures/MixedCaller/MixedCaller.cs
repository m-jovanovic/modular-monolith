using EquipmentLibrary.Catalog;
using EquipmentLibrary.Catalog.Contracts;

namespace EquipmentLibrary.Lending.Probe;

// This compiled canary has both allowed and forbidden dependencies on the same type.
// Its project also has an unused PickupDesk implementation reference.
public static class MixedCaller
{
    public static (Type Allowed, Type Forbidden) Dependencies() => (typeof(ICatalogLookup), typeof(CatalogModule));
}
