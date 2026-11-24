using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace EquipmentLibrary.Architecture.Tests;

internal sealed record ProductionProject(
    string Path, string Assembly, List<string> Declared, List<string> Compiled);

internal static class ProjectGraph
{
    internal static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "EquipmentLibrary.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Expected the reference solution above the test assembly.");
    }

    internal static async Task<List<ProductionProject>> DiscoverAsync(string root, string configuration)
    {
        IEnumerable<string> projects = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Relative(root, path).Split('/').Any(segment => segment is "obj" or "bin"))
            .Order(StringComparer.Ordinal);
        var result = new List<ProductionProject>();
        foreach (string project in projects)
        {
            result.Add(await ReadAsync(root, project, configuration));
        }

        return result;
    }

    internal static async Task<ProductionProject> ReadAsync(string root, string path, string configuration)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in new[] { "msbuild", path, "-nologo", $"-p:Configuration={configuration}",
            "-getProperty:AssemblyName,TargetPath", "-getItem:ProjectReference" })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch dotnet msbuild.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Timed out evaluating {path}.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not evaluate {path}. {await errors}");
        }

        using var doc = JsonDocument.Parse(await output);
        JsonElement properties = doc.RootElement.GetProperty("Properties");
        string assembly = properties.GetProperty("AssemblyName").GetString()!;
        string binary = properties.GetProperty("TargetPath").GetString()!;
        if (!File.Exists(binary))
        {
            throw new FileNotFoundException($"Discovered {path}, but the {configuration} build did not produce {binary}. Include this project in the solution and the architecture check's build dependencies.");
        }

        List<string> declared = doc.RootElement.GetProperty("Items").GetProperty("ProjectReference").EnumerateArray()
            .Select(item => Relative(root, item.GetProperty("FullPath").GetString()!)).ToList();
        using FileStream stream = File.OpenRead(binary);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        List<string> compiled = metadata.AssemblyReferences.Select(handle =>
            metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToList();
        return new ProductionProject(Relative(root, path), assembly, declared, compiled);
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
