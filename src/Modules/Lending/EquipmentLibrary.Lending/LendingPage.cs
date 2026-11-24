namespace EquipmentLibrary.Lending;

internal sealed record LendingPage<T>(IReadOnlyList<T> Items);
