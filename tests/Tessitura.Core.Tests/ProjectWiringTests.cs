using System.Reflection;
using Xunit;

namespace Tessitura.Core.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void ReferencedAssemblyCanLoad()
    {
        Assembly assembly = Assembly.Load("Tessitura.Core");
        Assert.Equal("Tessitura.Core", assembly.GetName().Name);
    }
}
