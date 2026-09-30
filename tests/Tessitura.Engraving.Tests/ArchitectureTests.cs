using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ArchitectureTests
{
    [Theory]
    [InlineData("Tessitura.Core")]
    [InlineData("Tessitura.Smufl")]
    [InlineData("Tessitura.Engraving")]
    public void DomainAssembliesDoNotDependOnAvaloniaOrSkiaSharp(string projectName)
    {
        Assembly assembly = Assembly.Load(projectName);
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            Assert.False(IsForbidden(reference.Name), $"{projectName} references {reference.Name}");
        }

        string projectFile = Path.Combine(FindRepositoryRoot(), "src", projectName, $"{projectName}.csproj");
        XDocument project = XDocument.Load(projectFile);
        foreach (XElement element in project.Descendants())
        {
            string kind = element.Name.LocalName;
            if (kind is not ("PackageReference" or "Reference" or "ProjectReference"))
            {
                continue;
            }

            string? include = (string?)element.Attribute("Include");
            Assert.False(IsForbidden(include), $"{projectName} declares {include}");
        }
    }

    private static bool IsForbidden(string? name) =>
        name?.Contains("Avalonia", StringComparison.OrdinalIgnoreCase) == true ||
        name?.Contains("SkiaSharp", StringComparison.OrdinalIgnoreCase) == true;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tessitura.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Tessitura.sln was not found above the test directory.");
    }
}
