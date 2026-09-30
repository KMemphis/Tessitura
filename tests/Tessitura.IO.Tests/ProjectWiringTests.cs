using System.Reflection;
using Xunit;

namespace Tessitura.IO.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void ReferencedAssemblyCanLoad()
    {
        Assembly assembly = Assembly.Load("Tessitura.IO");
        Assert.Equal("Tessitura.IO", assembly.GetName().Name);
    }
}
