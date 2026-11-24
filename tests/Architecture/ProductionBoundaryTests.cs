using System.Reflection;
using Xunit;

namespace EquipmentLibrary.Architecture.Tests;

public sealed class ProductionBoundaryTests
{
    internal static string Configuration => typeof(ProductionBoundaryTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;

    [Fact]
    public async Task ProductionProjectsOnlyDependOnApprovedModuleContracts()
    {
        string root = ProjectGraph.FindRoot();
        List<ProductionProject> projects = await ProjectGraph.DiscoverAsync(root, Configuration);
        Assert.Empty(BoundaryPolicy.Load(root).Check(projects));
    }
}
