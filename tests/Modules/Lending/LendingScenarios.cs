using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[CollectionDefinition(nameof(LendingScenarios))]
public sealed class LendingScenarios : ICollectionFixture<LendingFixture>;
