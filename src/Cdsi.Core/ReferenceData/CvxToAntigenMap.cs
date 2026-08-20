using Cdsi.Core.Common;

namespace Cdsi.Core.ReferenceData;

/// <summary>
/// One &lt;cvxMap&gt; entry from the Schedule supporting data — maps a CVX code to the
/// antigen(s) it counts toward. Almost always a single unconditional association (or several,
/// for combination vaccines like DTaP -> Diphtheria+Tetanus+Pertussis). The one documented
/// exception in the current data is CVX 121 (Zoster live), which maps to Varicella below age 50
/// and Zoster at/above age 50 — see CvxAssociation.AppliesAt.
/// </summary>
public sealed class CvxMapEntry
{
    public required string Cvx { get; init; }
    public string? ShortDescription { get; init; }
    public required IReadOnlyList<CvxAssociation> Associations { get; init; }
}

/// <summary>
/// One antigen association for a CVX code, optionally gated by the patient's age at the date
/// administered. This is NOT the same kind of range as ITemporallyVersioned (that's a
/// calendar-date effective/cessation window selecting between rule *versions*); this is an
/// age-since-birth window selecting between antigens for the same physical product. Kept as a
/// distinct, purpose-built check rather than forced under the same interface.
/// </summary>
public sealed class CvxAssociation
{
    public required string Antigen { get; init; }
    public DurationExpression? AssociationBeginAge { get; init; }
    public DurationExpression? AssociationEndAge { get; init; }

    public bool IsAgeGated => AssociationBeginAge is not null || AssociationEndAge is not null;

    /// <summary>Does this association apply for a dose given at the given age (dob, dateAdministered)?</summary>
    public bool AppliesAt(DateOnly dob, DateOnly dateAdministered)
    {
        if (!IsAgeGated)
        {
            return true;
        }

        var begin = AssociationBeginAge?.AddTo(dob) ?? dob;
        var end = AssociationEndAge?.AddTo(dob) ?? DateOnly.MaxValue;
        return dateAdministered >= begin && dateAdministered < end;
    }
}
