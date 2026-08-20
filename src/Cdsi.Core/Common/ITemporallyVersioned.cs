namespace Cdsi.Core.Common;

/// <summary>
/// Implemented by any supporting-data element that can carry multiple time-boxed instances
/// (Logic Spec §3.3 "Selecting Supporting Data" — applies to Age, Preferable Interval,
/// Allowable Interval, and Conditional Skip attributes, and by the same pattern, the
/// CVX-to-antigen association ages in the Schedule supporting data).
/// </summary>
public interface ITemporallyVersioned
{
    DateOnly? EffectiveDate { get; }
    DateOnly? CessationDate { get; }
}
