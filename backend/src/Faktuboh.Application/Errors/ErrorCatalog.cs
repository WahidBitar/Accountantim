using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Faktuboh.Application.Errors;

public sealed record ErrorCatalogEntry(string Code, string Title, int HttpStatus);

public static class ErrorCatalog
{
    private static readonly FrozenDictionary<string, ErrorCatalogEntry> Entries =
        new Dictionary<string, ErrorCatalogEntry>(StringComparer.Ordinal)
            .ToFrozenDictionary(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, ErrorCatalogEntry> All => Entries;

    public static bool TryGet(string code, [NotNullWhen(true)] out ErrorCatalogEntry? entry)
    {
        if (string.IsNullOrEmpty(code))
        {
            entry = null;
            return false;
        }

        return Entries.TryGetValue(code, out entry);
    }
}
