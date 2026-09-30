using System.Reflection;
using Xunit;

namespace Tessitura.Engraving.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void ReferencedAssemblyCanLoad()
    {
        Assembly assembly = Assembly.Load("Tessitura.Engraving");
        Assert.Equal("Tessitura.Engraving", assembly.GetName().Name);
    }
}
