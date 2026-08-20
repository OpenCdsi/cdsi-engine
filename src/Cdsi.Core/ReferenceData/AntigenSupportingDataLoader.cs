using System.Xml.Linq;
using Cdsi.Core.Models;

namespace Cdsi.Core.ReferenceData;

/// <summary>Loads one AntigenSupportingData-*.xml file into its list of AntigenSeries. Only the fields needed by §4.2/§5.1 (and the Age/Interval fields modeled for the upcoming §6.4-6.6 work) are parsed — contraindications, immunity evidence, and vaccine preference lists are not yet modeled.</summary>
public static class AntigenSupportingDataLoader
{
    public static IReadOnlyList<AntigenSeries> LoadFile(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidOperationException($"'{path}' has no root element.");
        return ParseSeriesList(root, path);
    }

    private static IReadOnlyList<AntigenSeries> ParseSeriesList(XElement root, string sourcePath)
    {
        var seriesElements = root.Element("series") is not null
            ? root.Elements("series")
            : Enumerable.Empty<XElement>();

        var result = new List<AntigenSeries>();
        foreach (var seriesEl in seriesElements)
        {
            result.Add(ParseSeries(seriesEl, sourcePath));
        }
        return result;
    }

    private static AntigenSeries ParseSeries(XElement seriesEl, string sourcePath)
    {
        var seriesName = seriesEl.ElementTextOrNull("seriesName")
            ?? throw new InvalidOperationException($"Series with no seriesName in '{sourcePath}'.");
        var antigen = seriesEl.ElementTextOrNull("targetDisease")
            ?? throw new InvalidOperationException($"Series '{seriesName}' in '{sourcePath}' has no targetDisease.");
        var seriesTypeText = seriesEl.ElementTextOrNull("seriesType")
            ?? throw new InvalidOperationException($"Series '{seriesName}' in '{sourcePath}' has no seriesType.");

        var requiredGenders = seriesEl.Elements("requiredGender")
            .Select(e => e.Value)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(XmlParsingHelpers.ParseGender)
            .Distinct()
            .ToArray();

        var indications = seriesEl.Elements("indication")
            .Select(ParseIndication)
            .ToArray();

        var seriesDoses = seriesEl.Elements("seriesDose")
            .Select(ParseSeriesDose)
            .ToArray();

        return new AntigenSeries
        {
            SeriesName = seriesName,
            Antigen = antigen,
            TargetDisease = antigen,
            VaccineGroup = seriesEl.ElementTextOrNull("vaccineGroup"),
            SeriesType = XmlParsingHelpers.ParseSeriesType(seriesTypeText),
            RequiredGenders = requiredGenders,
            Indications = indications,
            SeriesDoses = seriesDoses
        };
    }

    private static Indication ParseIndication(XElement el)
    {
        var obsCodeEl = el.Element("observationCode");
        return new Indication
        {
            ObservationCode = obsCodeEl?.ElementTextOrNull("code"),
            Description = el.ElementTextOrNull("description"),
            BeginAge = el.ParseDurationOrNull("beginAge"),
            EndAge = el.ParseDurationOrNull("endAge")
        };
    }

    private static SeriesDose ParseSeriesDose(XElement el)
    {
        var doseNumberText = el.ElementTextOrNull("doseNumber")
            ?? throw new InvalidOperationException("seriesDose element missing doseNumber.");

        return new SeriesDose
        {
            DoseNumber = XmlParsingHelpers.ParseDoseNumber(doseNumberText),
            AgeRules = el.Elements("age").Where(HasChildren).Select(ParseAgeRule).ToArray(),
            PreferableIntervals = el.Elements("interval").Where(HasChildren).Select(ParsePreferableInterval).ToArray(),
            AllowableIntervals = el.Elements("allowableInterval").Where(HasChildren).Select(ParseAllowableInterval).ToArray()
        };
    }

    /// <summary>
    /// The supporting data represents "this attribute doesn't apply to this dose" with an
    /// empty self-closing placeholder element (e.g. &lt;interval/&gt; on a Dose 1, which has no
    /// previous dose to measure from) rather than omitting the element entirely. Filter these
    /// out before parsing rather than treating them as malformed structured data.
    /// </summary>
    private static bool HasChildren(XElement el) => el.Elements().Any();

    private static AgeRule ParseAgeRule(XElement el) => new()
    {
        EffectiveDate = el.ParseDateOrNull("effectiveDate"),
        CessationDate = el.ParseDateOrNull("cessationDate"),
        AbsMinAge = el.ParseDurationOrNull("absMinAge"),
        MinAge = el.ParseDurationOrNull("minAge"),
        MaxAge = el.ParseDurationOrNull("maxAge")
    };

    private static PreferableIntervalRule ParsePreferableInterval(XElement el)
    {
        var (refType, refDoseNum, refCvxCodes, refObsCode) = ParseIntervalReference(el, allowMostRecentAndObs: true);
        return new PreferableIntervalRule
        {
            EffectiveDate = el.ParseDateOrNull("effectiveDate"),
            CessationDate = el.ParseDateOrNull("cessationDate"),
            ReferenceType = refType,
            ReferenceTargetDoseNumber = refDoseNum,
            ReferenceVaccineCvxCodes = refCvxCodes,
            ReferenceObservationCode = refObsCode,
            AbsMinInt = el.ParseDurationOrNull("absMinInt"),
            MinInt = el.ParseDurationOrNull("minInt")
        };
    }

    private static AllowableIntervalRule ParseAllowableInterval(XElement el)
    {
        var (refType, refDoseNum, _, _) = ParseIntervalReference(el, allowMostRecentAndObs: false);
        return new AllowableIntervalRule
        {
            EffectiveDate = el.ParseDateOrNull("effectiveDate"),
            CessationDate = el.ParseDateOrNull("cessationDate"),
            ReferenceType = refType,
            ReferenceTargetDoseNumber = refDoseNum,
            AbsMinInt = el.ParseDurationOrNull("absMinInt")
        };
    }

    private static (IntervalReferenceType, int?, IReadOnlyList<string>, string?) ParseIntervalReference(XElement el, bool allowMostRecentAndObs)
    {
        var fromPrevious = el.ElementTextOrNull("fromPrevious");
        if (string.Equals(fromPrevious, "Y", StringComparison.OrdinalIgnoreCase))
        {
            return (IntervalReferenceType.FromPrevious, null, Array.Empty<string>(), null);
        }

        var fromTargetDose = el.ElementTextOrNull("fromTargetDose");
        if (fromTargetDose is not null)
        {
            return (IntervalReferenceType.FromTargetDose, XmlParsingHelpers.ParseDoseNumber(fromTargetDose), Array.Empty<string>(), null);
        }

        if (allowMostRecentAndObs)
        {
            var fromMostRecent = el.ElementTextOrNull("fromMostRecent");
            if (fromMostRecent is not null)
            {
                return (IntervalReferenceType.FromMostRecent, null, XmlParsingHelpers.ParseCvxList(fromMostRecent), null);
            }

            var fromRelevantObsEl = el.Element("fromRelevantObs");
            var obsCode = fromRelevantObsEl?.ElementTextOrNull("code");
            if (obsCode is not null)
            {
                return (IntervalReferenceType.FromRelevantObservation, null, Array.Empty<string>(), obsCode);
            }
        }

        throw new InvalidOperationException(
            "Interval element specifies no recognizable reference point (fromPrevious/fromTargetDose" +
            (allowMostRecentAndObs ? "/fromMostRecent/fromRelevantObs" : "") + ").");
    }
}
