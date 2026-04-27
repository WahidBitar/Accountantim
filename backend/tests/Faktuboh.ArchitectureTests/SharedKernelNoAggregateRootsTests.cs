using Faktuboh.Domain.Primitives;

namespace Faktuboh.ArchitectureTests;

public class SharedKernelNoAggregateRootsTests
{
    [Fact]
    public void Domain_assembly_holds_no_IAggregateRoot_implementations()
    {
        var domainTypes = typeof(Money).Assembly.GetTypes();

        // Sentinel: prove the test actually scanned types (catches future trimming/empty-assembly false-greens).
        Assert.True(
            domainTypes.Length > 0,
            "Domain assembly scanned no types — IAggregateRoot fitness test would pass vacuously.");

        // Match by full name to avoid collisions with same-named interfaces in third-party assemblies.
        var offenders = domainTypes
            .Where(t => t.GetInterfaces().Any(i => i.FullName == "Faktuboh.Domain.IAggregateRoot"))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Shared Kernel must hold no IAggregateRoot types. Offenders: {string.Join(", ", offenders)}");
    }
}
