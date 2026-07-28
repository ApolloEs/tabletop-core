using System.Reflection;
using Xunit;

namespace TabletopCore.Engine.Tests;

public class ScaffoldSmokeTests
{
    [Fact]
    public void EngineAssemblyIsReferencedAndLoads()
    {
        var assembly = Assembly.Load("TabletopCore.Engine");
        Assert.Equal("TabletopCore.Engine", assembly.GetName().Name);
    }
}
