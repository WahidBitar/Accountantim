using System.Collections.Frozen;
using System.Collections.Generic;

namespace Faktuboh.Domain.Primitives;

/// <summary>
/// Authoritative registry of supported ISO 4217 currencies and their minor-unit counts.
/// </summary>
/// <remarks>
/// <b>Currency codes are case-sensitive (ISO 4217 upper-case).</b> Lower-case input
/// (e.g. <c>"eur"</c>) is intentionally rejected. Callers must canonicalize at boundaries.
/// </remarks>
public static class CurrencyRegistry
{
    private static readonly FrozenDictionary<string, int> Currencies =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["EUR"] = 2,
            ["USD"] = 2,
            ["GBP"] = 2,
            ["AED"] = 2,
            ["SAR"] = 2,
            ["EGP"] = 2,
            ["JOD"] = 3,
            ["KWD"] = 3,
            ["BHD"] = 3,
            ["TND"] = 3,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, int> All => Currencies;

    public static bool IsSupported(string currency) => Currencies.ContainsKey(currency);

    public static int MinorUnits(string currency) =>
        Currencies.TryGetValue(currency, out var minor)
            ? minor
            : throw new ArgumentException($"Unsupported currency '{currency}'", nameof(currency));

    /// <summary>
    /// Atomic lookup combining <see cref="IsSupported"/> + <see cref="MinorUnits"/>.
    /// Preferred form for callers that need both signals (e.g. <see cref="Money"/> ctor),
    /// avoiding the redundant frozen-dictionary lookup of the check-then-fetch pattern.
    /// </summary>
    public static bool TryGetMinorUnits(string currency, out int minorUnits) =>
        Currencies.TryGetValue(currency, out minorUnits);
}
