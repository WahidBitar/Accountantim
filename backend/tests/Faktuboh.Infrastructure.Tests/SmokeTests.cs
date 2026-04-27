using System.Reflection;

namespace Faktuboh.Infrastructure.Tests;

public class SmokeTests
{
    [Fact]
    public void Project_assembly_loads()
    {
        var assembly = Assembly.Load("Faktuboh.Infrastructure");
        Assert.NotNull(assembly);
    }
}
