namespace Cdsi.Core.Evaluation;

/// <summary>
/// §8.4 Complete Patient Series scoring (Table 8-7, SELECTB-19). The smallest of the three
/// point-scoring tables - a single condition ("has the most valid doses"): +1 if this series
/// uniquely has the most, 0 if tied with one or more others for the most, -1 otherwise.
/// </summary>
public static class ScoreCompletePatientSeries
{
    /// <param name="thisSeriesValidDoseCount">Must be one of the values in allValidDoseCountsInGroup.</param>
    /// <param name="allValidDoseCountsInGroup">The valid dose count (SELECTB-21) for every scorable patient series being scored in this group, including this one.</param>
    public static int Execute(int thisSeriesValidDoseCount, IReadOnlyList<int> allValidDoseCountsInGroup)
    {
        var max = allValidDoseCountsInGroup.Max();

        if (thisSeriesValidDoseCount < max)
        {
            return -1;
        }

        var seriesAtMax = allValidDoseCountsInGroup.Count(c => c == max);
        return seriesAtMax == 1 ? 1 : 0;
    }
}
