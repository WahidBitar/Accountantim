using Faktuboh.Domain.Primitives;

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

        // Use assembly-name matching (not namespace-prefix matching) to avoid two failure
        // modes of NetArchTest's HaveDependencyOnAny string-prefix approach:
        //   1. False positive: a future namespace `Faktuboh.ApiContracts` (or any
        //      `Faktuboh.Api…` namespace not separated by a dot from a sibling-project
        //      assembly name) would falsely trip a `"Faktuboh.Api"` prefix rule.
        //   2. False negative: renaming a sibling assembly (e.g. `Faktuboh.Api` →
        //      `Faktuboh.ApiV2`) silently passes the test against the old prefix.
        // Assembly-name matching with a strict trailing-dot prefix is unambiguous: it
        // matches the compiled identity, not stringly-typed namespace text.
        var leakedReferences = domainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null && name.StartsWith("Faktuboh.", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            leakedReferences.Count == 0,
            $"Domain assembly leaked dependency on: {string.Join(", ", leakedReferences)}");
    }
}
