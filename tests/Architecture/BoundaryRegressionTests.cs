using Xunit;

namespace EquipmentLibrary.Architecture.Tests;

public sealed class BoundaryRegressionTests
{
    [Fact]
    public async Task MixedCallerRejectsForeignImplementationAndUnusedProjectEdges()
    {
        string root = ProjectGraph.FindRoot();
        const string project = "tests/Architecture/Fixtures/MixedCaller/EquipmentLibrary.Lending.Probe.csproj";
        ProductionProject probe = await ProjectGraph.ReadAsync(root, Path.Combine(root, project), ProductionBoundaryTests.Configuration);
        Assert.Contains("EquipmentLibrary.Catalog.Contracts", probe.Compiled);
        Assert.Contains("EquipmentLibrary.Catalog", probe.Compiled);
        Assert.DoesNotContain("EquipmentLibrary.PickupDesk", probe.Compiled);
        List<ProductionProject> graph = await ProjectGraph.DiscoverAsync(root, ProductionBoundaryTests.Configuration);
        graph.Add(probe with { Path = "src/Modules/Lending/Probe/EquipmentLibrary.Lending.Probe.csproj" });
        List<string> errors = BoundaryPolicy.Load(root).Check(graph);
        Assert.Contains(errors, e => e.Contains("Catalog/Implementation/", StringComparison.Ordinal) &&
            e.Contains("compiled reference", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("PickupDesk/PickupDesk/", StringComparison.Ordinal) &&
            e.Contains("project reference", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains("Catalog/Contracts/", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationCanDependOnDomainButCannotImportTheHttpOrPersistenceAdapter()
    {
        var domain = new ProductionProject("src/Modules/Lending/Domain/Domain.csproj", "EquipmentLibrary.Lending.Domain", [], []);
        var adapter = new ProductionProject("src/Modules/Lending/Storage/Storage.csproj", "EquipmentLibrary.Lending.Storage", [], []);
        var app = new ProductionProject("src/Modules/Lending/Application/Application.csproj", "EquipmentLibrary.Lending.Application",
            [domain.Path, adapter.Path], ["Microsoft.AspNetCore.Http", "Npgsql"]);
        List<string> errors = BoundaryPolicy.Load(ProjectGraph.FindRoot()).Check([domain, adapter, app]);
        Assert.Contains(errors, e => e.Contains("application cannot depend on a persistence or HTTP adapter", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Microsoft.AspNetCore.Http: application cannot depend on infrastructure", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Npgsql: application cannot depend on infrastructure", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, e => e.Contains($"-> {domain.Path}", StringComparison.Ordinal));
    }

    [Fact]
    public void UnregisteredModuleIsReportedEvenWhenItHasNoDependencies()
    {
        var project = new ProductionProject("src/Modules/Gifts/EquipmentLibrary.Gifts.csproj", "EquipmentLibrary.Gifts", [], []);
        List<string> errors = BoundaryPolicy.Load(ProjectGraph.FindRoot()).Check([project]);
        Assert.Contains(errors, e => e.Contains("Unregistered production module or project: src/Modules/Gifts", StringComparison.Ordinal));
    }

    [Fact]
    public void OutwardDomainDependencyAndCycleAreReported()
    {
        var domain = new ProductionProject("src/Modules/Lending/Domain/EquipmentLibrary.Lending.Domain.csproj", "EquipmentLibrary.Lending.Domain",
            ["src/Modules/Lending/Storage/EquipmentLibrary.Lending.Storage.csproj"], ["Npgsql"]);
        var storage = new ProductionProject("src/Modules/Lending/Storage/EquipmentLibrary.Lending.Storage.csproj", "EquipmentLibrary.Lending.Storage",
            [domain.Path], []);
        List<string> errors = BoundaryPolicy.Load(ProjectGraph.FindRoot()).Check([domain, storage]);
        Assert.Contains(errors, e => e.Contains("domain cannot depend outward", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Npgsql: domain cannot depend on infrastructure", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Dependency cycle:", StringComparison.Ordinal));
    }
}
