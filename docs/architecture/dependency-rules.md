# Module dependencies

Run `./scripts/verify.sh tests/Architecture/EquipmentLibrary.Architecture.Tests.csproj`. The test discovers production project files under src, asks MSBuild for the evaluated references and output path, and reads assembly references from the compiled PE metadata. It checks each edge separately. The one named composition root can register implementations. The dependency graph must remain acyclic.

The [policy](../../tests/Architecture/module-policy.json) lists module ownership and published contracts. A new module must enter the inventory. A public contract can reference another module's contract only when the import is declared. An implementation can use its own module's types and declared peer contracts. A Domain project must avoid infrastructure assemblies and outward references.

The [regression fixture](../../tests/Architecture/Fixtures/MixedCaller/MixedCaller.cs) deliberately combines an allowed Catalog lookup and a forbidden Catalog implementation on one type. Its MSBuild project also references the PickupDesk implementation without using a type. The suite requires a failure for each forbidden edge and checks that the permitted edge remains legal. [Regression tests](../../tests/Architecture/BoundaryRegressionTests.cs)
