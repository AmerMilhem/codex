using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Erp.ArchitectureTests;

public sealed class DependencyTests
{
    private static string Root
    {
        get { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "Erp.slnx"))) d = d.Parent; return d?.FullName ?? throw new Exception("Repository root missing."); }
    }
    public static bool Allowed(string from, string to)
    {
        if (from == to) return false;
        if (from == "Erp.Kernel") return false;
        if (from == "Erp.Application.Abstractions") return to == "Erp.Kernel";
        var m = Regex.Match(from, @"^Erp\.(\w+)\.(Domain|Contracts|Application|Infrastructure)$");
        if (!m.Success) return true; // hosts and tests compose adapters
        var module = m.Groups[1].Value; var layer = m.Groups[2].Value;
        if (layer is "Domain" or "Contracts") return to == "Erp.Kernel";
        if (to is "Erp.Kernel" or "Erp.Application.Abstractions") return true;
        // Shared database/UoW adapters are a building block, not another business module's persistence.
        if (layer == "Infrastructure" && to == "Erp.Platform.Infrastructure") return true;
        if (to.StartsWith($"Erp.{module}.", StringComparison.Ordinal)) return layer == "Infrastructure"
            ? to.EndsWith(".Application", StringComparison.Ordinal) || to.EndsWith(".Domain", StringComparison.Ordinal) || to.EndsWith(".Contracts", StringComparison.Ordinal)
            : to.EndsWith(".Domain", StringComparison.Ordinal) || to.EndsWith(".Contracts", StringComparison.Ordinal);
        return layer == "Application" && module == "Sales" && to is "Erp.Finance.Contracts" or "Erp.Inventory.Contracts";
    }
    [Fact]
    public void Every_project_reference_obeys_module_and_layer_rules()
    {
        var projects = Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories);
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project); var xml = XDocument.Load(project);
            if (project.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                Assert.Matches(@"^Erp\.[A-Za-z]+\.(Domain|Contracts|Application|Infrastructure)$", name);
            foreach (var reference in xml.Descendants("ProjectReference"))
            {
                var target = Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value);
                Assert.True(Allowed(name, target), $"Forbidden dependency {name} -> {target}");
            }
            if (name.EndsWith(".Domain", StringComparison.Ordinal) || name.EndsWith(".Contracts", StringComparison.Ordinal)
                || name.EndsWith(".Application", StringComparison.Ordinal) || name is "Erp.Kernel" or "Erp.Application.Abstractions")
            {
                Assert.Empty(xml.Descendants("PackageReference")); Assert.Empty(xml.Descendants("FrameworkReference"));
                foreach (var source in Directory.GetFiles(Path.GetDirectoryName(project)!, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
                    Assert.DoesNotMatch(@"using\s+(Npgsql|Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|System\.Net\.Http)", File.ReadAllText(source));
            }
        }
    }
    [Fact]
    public void Compiled_domain_and_contract_assemblies_do_not_reference_persistence_or_other_implementations()
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        foreach (var project in Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(project);
            if (!(name.EndsWith(".Domain", StringComparison.Ordinal) || name.EndsWith(".Contracts", StringComparison.Ordinal) || name.EndsWith(".Application", StringComparison.Ordinal)
                || name == "Erp.Kernel" || name == "Erp.Application.Abstractions")) continue;
            var assembly = System.Reflection.Assembly.LoadFile(Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration, "net10.0", name + ".dll"));
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                Assert.False(reference.Name!.StartsWith("Npgsql", StringComparison.Ordinal) || reference.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    || reference.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal), $"Forbidden runtime dependency {name} -> {reference.Name}");
                if (reference.Name.StartsWith("Erp.", StringComparison.Ordinal)) Assert.True(Allowed(name, reference.Name), $"Forbidden assembly dependency {name} -> {reference.Name}");
            }
        }
    }
    [Theory]
    [InlineData("Erp.Sales.Domain", "Erp.Sales.Infrastructure")]
    [InlineData("Erp.Sales.Application", "Erp.Finance.Infrastructure")]
    [InlineData("Erp.Sales.Application", "Erp.Inventory.Infrastructure")]
    [InlineData("Erp.Inventory.Infrastructure", "Erp.Finance.Infrastructure")]
    [InlineData("Erp.Finance.Contracts", "Erp.Finance.Domain")]
    public void Injected_violations_are_rejected(string from, string to) => Assert.False(Allowed(from, to));

    [Fact]
    public void Project_graph_is_acyclic()
    {
        var graph = Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetFileNameWithoutExtension(f)!, f => XDocument.Load(f).Descendants("ProjectReference")
                .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value)!).ToArray());
        void Visit(string node, HashSet<string> path)
        {
            Assert.True(path.Add(node), $"Dependency cycle through {node}");
            if (graph.TryGetValue(node, out var children)) foreach (var child in children) Visit(child, path);
            path.Remove(node);
        }
        foreach (var key in graph.Keys) Visit(key!, []);
    }
    [Fact]
    public void Web_and_hosts_do_not_contain_direct_database_access()
    {
        var web = Path.Combine(Root, "src", "Clients", "Web");
        foreach (var source in Directory.Exists(web) ? Directory.GetFiles(Path.Combine(web, "src"), "*.ts", SearchOption.AllDirectories) : [])
            Assert.DoesNotMatch("""from\s+['"](?:pg|postgres|typeorm|@prisma/client)['"]""", File.ReadAllText(source));
        foreach (var host in new[] { "Erp.Api", "Erp.Worker" })
            foreach (var source in Directory.GetFiles(Path.Combine(Root, "src", "Hosts", host), "*.cs", SearchOption.TopDirectoryOnly))
                Assert.DoesNotMatch(@"NpgsqlCommand|ExecuteSql|SELECT\s+.+FROM|\.SaveChanges", File.ReadAllText(source));
    }
}
