using Faktuboh.Domain.Primitives;
using NetArchTest.Rules;

namespace Faktuboh.ArchitectureTests;

public class SharedKernelPurityTests
{
    [Fact]
    public void Domain_assembly_does_not_depend_on_any_other_Faktuboh_assembly()
    {
        var domainAssembly = typeof(Money).Assembly;

        // Sentinel: prove the test actually scanned types (catches future trimming/empty-assembly false-greens).
        Assert.True(
            domainAssembly.GetTypes().Length > 0,
            "Domain assembly scanned no types — purity fitness test would pass vacuously.");

        var result = Types.InAssembly(domainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Faktuboh.Api",
                "Faktuboh.Application",
                "Faktuboh.Infrastructure",
                "Faktuboh.Contracts",
                "Faktuboh.AppHost",
                "Faktuboh.ServiceDefaults")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain assembly leaked a dependency on: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
