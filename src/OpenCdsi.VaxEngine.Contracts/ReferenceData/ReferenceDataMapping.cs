/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Contracts.ReferenceData;

/// <summary>Maps OpenCdsi.VaxEngine.Core.ReferenceData's real models to this project's reference-data DTOs - kept as one small, testable static class rather than inlined into endpoint handlers, same reasoning as RequestMapping/ResponseMapping.</summary>
public static class ReferenceDataMapping
{
    public static AntigenSummaryDto ToSummaryDto(string antigenName, int seriesCount) => new()
    {
        Name = antigenName,
        SeriesCount = seriesCount
    };

    public static AntigenSeriesDto ToDto(AntigenSeries series) => new()
    {
        SeriesName = series.SeriesName,
        Antigen = series.Antigen,
        TargetDisease = series.TargetDisease,
        VaccineGroup = series.VaccineGroup,
        SeriesType = series.SeriesType.ToString(),
        RequiredGenders = series.RequiredGenders.Select(g => g.ToString()).ToArray(),
        Indications = series.Indications.Select(ToDto).ToArray(),
        SeriesDoses = series.SeriesDoses.Select(ToDto).ToArray(),
        SeriesAdminGuidance = series.SeriesAdminGuidance,
        SeriesGroupInfo = ToDto(series.SeriesGroupInfo),
        EquivalentSeriesGroup = series.EquivalentSeriesGroup
    };

    private static IndicationDto ToDto(Indication indication) => new()
    {
        ObservationCode = indication.ObservationCode,
        Description = indication.Description,
        BeginAge = indication.BeginAge?.ToString(),
        EndAge = indication.EndAge?.ToString(),
        Guidance = indication.Guidance
    };

    private static SeriesGroupInfoDto ToDto(SeriesGroupInfo info) => new()
    {
        IsDefaultSeries = info.IsDefaultSeries,
        IsProductPath = info.IsProductPath,
        SeriesGroupName = info.SeriesGroupName,
        SeriesGroup = info.SeriesGroup,
        SeriesPriority = info.SeriesPriority,
        SeriesPreference = info.SeriesPreference,
        MinAgeToStart = info.MinAgeToStart?.ToString(),
        MaxAgeToStart = info.MaxAgeToStart?.ToString()
    };

    private static SeriesDoseDto ToDto(SeriesDose dose) => new()
    {
        DoseNumber = dose.DoseNumber,
        AgeRules = dose.AgeRules.Select(ToDto).ToArray(),
        PreferableIntervals = dose.PreferableIntervals.Select(ToDto).ToArray(),
        AllowableIntervals = dose.AllowableIntervals.Select(ToDto).ToArray(),
        InadvertentVaccineCvxCodes = dose.InadvertentVaccineCvxCodes,
        PreferableVaccines = dose.PreferableVaccines.Select(ToDto).ToArray(),
        AllowableVaccines = dose.AllowableVaccines.Select(ToDto).ToArray(),
        ConditionalSkipInstances = dose.ConditionalSkipInstances.Select(ToDto).ToArray(),
        SeasonalRecommendation = dose.SeasonalRecommendation is { } rec ? ToDto(rec) : null,
        IsRecurringDose = dose.IsRecurringDose
    };

    private static SeasonalRecommendationDto ToDto(SeasonalRecommendation rec) => new()
    {
        StartDate = rec.StartDate,
        EndDate = rec.EndDate
    };

    private static PreferableVaccineDto ToDto(PreferableVaccine v) => new()
    {
        Cvx = v.Cvx,
        BeginAge = v.BeginAge?.ToString(),
        EndAge = v.EndAge?.ToString(),
        TradeName = v.TradeName,
        Volume = v.Volume,
        ForecastVaccineTypeFlag = v.ForecastVaccineTypeFlag
    };

    private static AllowableVaccineDto ToDto(AllowableVaccine v) => new()
    {
        Cvx = v.Cvx,
        BeginAge = v.BeginAge?.ToString(),
        EndAge = v.EndAge?.ToString()
    };

    private static AgeRuleDto ToDto(AgeRule rule) => new()
    {
        EffectiveDate = rule.EffectiveDate,
        CessationDate = rule.CessationDate,
        AbsMinAge = rule.AbsMinAge?.ToString(),
        MinAge = rule.MinAge?.ToString(),
        MaxAge = rule.MaxAge?.ToString(),
        EarliestRecAge = rule.EarliestRecAge?.ToString(),
        LatestRecAge = rule.LatestRecAge?.ToString()
    };

    private static PreferableIntervalRuleDto ToDto(PreferableIntervalRule rule) => new()
    {
        EffectiveDate = rule.EffectiveDate,
        CessationDate = rule.CessationDate,
        ReferenceType = rule.ReferenceType.ToString(),
        ReferenceTargetDoseNumber = rule.ReferenceTargetDoseNumber,
        ReferenceVaccineCvxCodes = rule.ReferenceVaccineCvxCodes,
        ReferenceObservationCode = rule.ReferenceObservationCode,
        AbsMinInt = rule.AbsMinInt?.ToString(),
        MinInt = rule.MinInt?.ToString(),
        EarliestRecInt = rule.EarliestRecInt?.ToString(),
        LatestRecInt = rule.LatestRecInt?.ToString(),
        IntervalPriority = rule.IntervalPriority
    };

    private static AllowableIntervalRuleDto ToDto(AllowableIntervalRule rule) => new()
    {
        EffectiveDate = rule.EffectiveDate,
        CessationDate = rule.CessationDate,
        ReferenceType = rule.ReferenceType.ToString(),
        ReferenceTargetDoseNumber = rule.ReferenceTargetDoseNumber,
        AbsMinInt = rule.AbsMinInt?.ToString()
    };

    private static ConditionalSkipInstanceDto ToDto(ConditionalSkipInstance instance) => new()
    {
        Context = instance.Context,
        SetLogic = instance.SetLogic?.ToString(),
        Sets = instance.Sets.Select(ToDto).ToArray()
    };

    private static ConditionalSkipSetDto ToDto(ConditionalSkipSet set) => new()
    {
        SetId = set.SetId,
        EffectiveDate = set.EffectiveDate,
        CessationDate = set.CessationDate,
        ConditionLogic = set.ConditionLogic?.ToString(),
        Conditions = set.Conditions.Select(ToDto).ToArray()
    };

    private static ConditionalSkipConditionDto ToDto(ConditionalSkipCondition condition) => new()
    {
        ConditionType = condition.ConditionType.ToString(),
        StartDate = condition.StartDate,
        EndDate = condition.EndDate,
        BeginAge = condition.BeginAge?.ToString(),
        EndAge = condition.EndAge?.ToString(),
        DoseCount = condition.DoseCount,
        DoseType = condition.DoseType?.ToString(),
        DoseCountLogic = condition.DoseCountLogic?.ToString(),
        VaccineTypeCvxCodes = condition.VaccineTypeCvxCodes,
        Interval = condition.Interval?.ToString(),
        SeriesGroups = condition.SeriesGroups
    };

    public static AntigenContraindicationsDto ToDto(AntigenContraindicationData data) => new()
    {
        AntigenLevel = data.AntigenLevel.Select(ToDto).ToArray(),
        VaccineLevel = data.VaccineLevel.Select(ToDto).ToArray()
    };

    private static AntigenContraindicationDto ToDto(AntigenContraindication c) => new()
    {
        ObservationCode = c.ObservationCode,
        ObservationTitle = c.ObservationTitle,
        ContraindicationText = c.ContraindicationText,
        ContraindicationGuidance = c.ContraindicationGuidance,
        BeginAge = c.BeginAge?.ToString(),
        EndAge = c.EndAge?.ToString()
    };

    private static VaccineContraindicationDto ToDto(VaccineContraindication c) => new()
    {
        ObservationCode = c.ObservationCode,
        ObservationTitle = c.ObservationTitle,
        ContraindicationText = c.ContraindicationText,
        ContraindicationGuidance = c.ContraindicationGuidance,
        ContraindicatedVaccines = c.ContraindicatedVaccines.Select(ToDto).ToArray()
    };

    private static ContraindicatedVaccineDto ToDto(ContraindicatedVaccine c) => new()
    {
        Cvx = c.Cvx,
        BeginAge = c.BeginAge?.ToString(),
        EndAge = c.EndAge?.ToString()
    };

    public static VaccineSummaryDto ToSummaryDto(CvxMapEntry entry) => new()
    {
        Cvx = entry.Cvx,
        ShortDescription = entry.ShortDescription
    };

    public static VaccineDto ToDto(CvxMapEntry entry) => new()
    {
        Cvx = entry.Cvx,
        ShortDescription = entry.ShortDescription,
        Associations = entry.Associations.Select(ToDto).ToArray()
    };

    private static CvxAssociationDto ToDto(CvxAssociation assoc) => new()
    {
        Antigen = assoc.Antigen,
        AssociationBeginAge = assoc.AssociationBeginAge?.ToString(),
        AssociationEndAge = assoc.AssociationEndAge?.ToString()
    };

    public static VaccineConflictDto ToDto(VaccineConflictRule rule) => new()
    {
        ConflictingVaccineType = rule.ConflictingVaccineType,
        ConflictingCvx = rule.ConflictingCvx,
        ImpactedVaccineType = rule.ImpactedVaccineType,
        ImpactedCvx = rule.ImpactedCvx,
        ConflictBeginInterval = rule.ConflictBeginInterval.ToString(),
        MinConflictEndInterval = rule.MinConflictEndInterval.ToString(),
        ConflictEndInterval = rule.ConflictEndInterval.ToString()
    };

    public static VaccineGroupSummaryDto ToSummaryDto(VaccineGroupInfo info) => new()
    {
        Name = info.Name
    };

    /// <summary>See VaccineGroupDto's own doc comment for why `antigens` must come from the caller (grouping AntigenSeries.VaccineGroup across all series), not from anything on VaccineGroupInfo itself.</summary>
    public static VaccineGroupDto ToDto(VaccineGroupInfo info, IEnumerable<string> antigens) => new()
    {
        Name = info.Name,
        AdministerFullVaccineGroup = info.AdministerFullVaccineGroup,
        Antigens = antigens.ToArray()
    };

    public static ObservationSummaryDto ToSummaryDto(Observation observation) => new()
    {
        ObservationCode = observation.ObservationCode,
        ObservationTitle = observation.ObservationTitle
    };

    public static ObservationDto ToDto(Observation observation) => new()
    {
        ObservationCode = observation.ObservationCode,
        ObservationTitle = observation.ObservationTitle,
        Group = observation.Group,
        IndicationText = observation.IndicationText,
        ContraindicationText = observation.ContraindicationText,
        ClarifyingText = observation.ClarifyingText,
        CodedValues = observation.CodedValues.Select(ToDto).ToArray()
    };

    private static CodedValueDto ToDto(CodedValue codedValue) => new()
    {
        Code = codedValue.Code,
        CodeSystem = codedValue.CodeSystem,
        Text = codedValue.Text
    };
}
