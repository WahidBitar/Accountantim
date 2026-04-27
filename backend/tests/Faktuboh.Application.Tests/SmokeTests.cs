using System.Reflection;

namespace Faktuboh.Application.Tests;

public class SmokeTests
{
    [Fact]
    public void Project_assembly_loads()
    {
        var assembly = Assembly.Load("Faktuboh.Application");
        Assert.NotNull(assembly);
    }
}
