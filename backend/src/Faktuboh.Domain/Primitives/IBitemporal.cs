namespace Faktuboh.Domain.Primitives;

/// <summary>
/// Marker interface for entities that track <b>bitemporal</b> validity (when the fact was
/// true in the world) and recording (when the system learned about it). See ADR-022.
/// </summary>
/// <remarks>
/// <para><b>Interval semantics:</b> <see cref="ValidFrom"/> and <see cref="ValidTo"/>
/// describe a half-open interval <c>[ValidFrom, ValidTo)</c>. The invariant
/// <c>ValidFrom &lt; ValidTo</c> (when <c>ValidTo</c> is non-null) is enforced by the
/// owning aggregate at construction; this interface itself does not validate.</para>
/// <para><b>Mutability rationale:</b> Property setters are intentionally exposed because
/// EF Core change-tracking requires property-level mutability; the Story 0.6 bitemporal
/// interceptor + MigrationService rely on this. Aggregates that <i>hold</i> an
/// <see cref="IBitemporal"/> record must expose only immutable copies via
/// <c>with { }</c> patterns to consumers. Do not mutate <see cref="IBitemporal"/>
/// properties directly outside the EF Core interceptor.</para>
/// </remarks>
public interface IBitemporal
{
    DateTimeOffset ValidFrom { get; set; }
    DateTimeOffset? ValidTo { get; set; }
    DateTimeOffset RecordedAt { get; set; }
}
