using System.Reflection;
using System.Xml.Linq;

namespace NovaWallet.UnitTests.Architecture;

public class ProjectDependencyTests
{
    private static readonly string[] FrameworkPrefixesBannedFromApplication =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Microsoft.AspNetCore",
    ];

    [Fact]
    public void Domain_references_no_other_project_or_package()
    {
        var project = Load("NovaWallet.Domain");

        Assert.Empty(ProjectReferences(project));
        Assert.Empty(PackageReferences(project));
    }

    [Fact]
    public void Application_references_only_domain()
    {
        var project = Load("NovaWallet.Application");

        Assert.Equal(["NovaWallet.Domain"], ProjectReferences(project));
    }

    [Fact]
    public void Application_references_no_persistence_or_web_package()
    {
        var packages = PackageReferences(Load("NovaWallet.Application"));

        Assert.DoesNotContain(packages, p => FrameworkPrefixesBannedFromApplication.Any(p.StartsWith));
    }

    [Fact]
    public void Application_assembly_loads_no_persistence_or_web_assembly()
    {
        var referenced = typeof(NovaWallet.Application.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(referenced, name => FrameworkPrefixesBannedFromApplication.Any(name.StartsWith)
            || name.StartsWith("NovaWallet.Infrastructure", StringComparison.Ordinal)
            || name.StartsWith("NovaWallet.Api", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_does_not_reference_api()
    {
        Assert.DoesNotContain("NovaWallet.Api", ProjectReferences(Load("NovaWallet.Infrastructure")));
    }

    private static XDocument Load(string projectName) => XDocument.Load(RepositoryLayout.ProjectFile(projectName));

    private static string[] ProjectReferences(XDocument project) =>
        project.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] PackageReferences(XDocument project) =>
        project.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToArray();
}
