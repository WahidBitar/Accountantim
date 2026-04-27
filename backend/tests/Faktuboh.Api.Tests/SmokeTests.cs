using System.Reflection;

namespace Faktuboh.Api.Tests;

public class SmokeTests
{
    [Fact]
    public void Project_assembly_loads()
    {
        var assembly = Assembly.Load("Faktuboh.Api");
        Assert.NotNull(assembly);
    }
}
