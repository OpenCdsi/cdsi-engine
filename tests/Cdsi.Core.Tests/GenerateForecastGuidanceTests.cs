using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class GenerateForecastGuidanceTests
{
    // Real data: "HepB 3-dose series" has seriesAdminGuidance "Anyone age 60 years or older
    // who does not meet risk-based recommendations may still receive Hepatitis B vaccination."
    private static readonly AntigenSeries HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
            .Single(s => s.SeriesName == "HepB 3-dose series");

    // Real data: "Hib risk 1-dose series" has an indication (code "002") with guidance
    // "Vaccination 14 or more days before splenectomy is suggested."
    private static readonly AntigenSeries HibRiskSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_Hib-508.xml"))
            .Single(s => s.SeriesName == "Hib risk 1-dose series");

    // Real data: Measles antigen-level contraindication "157" (Solid organ transplantation)
    // has real contraindicationGuidance text.
    private static readonly AntigenContraindicationData MeaslesContraindications =
        AntigenSupportingDataLoader.LoadContraindicationData(TestPaths.AntigenFile("AntigenSupportingData-_Measles-508.xml"));

    // Real data: Influenza vaccine-level contraindication "157" (also Solid organ
    // transplantation) has real contraindicationGuidance text too.
    private static readonly AntigenContraindicationData InfluenzaContraindications =
        AntigenSupportingDataLoader.LoadContraindicationData(TestPaths.AntigenFile("AntigenSupportingData-_Influenza-508.xml"));

    private static Patient MakePatient(IReadOnlyList<PatientObservation>? observations = null) => new()
    {
        PatientId = "p1",
        DateOfBirth = new DateOnly(1990, 1, 1),
        ActiveObservations = observations ?? Array.Empty<PatientObservation>()
    };

    [Fact]
    public void SeriesRegimenGuidance_AlwaysIncluded_RegardlessOfPatientObservations()
    {
        var patient = MakePatient();

        var result = GenerateForecastGuidance.Execute(HepBSeries, patient, Array.Empty<AntigenContraindication>(), Array.Empty<VaccineContraindication>());

        Assert.Contains("Anyone age 60 years or older who does not meet risk-based recommendations may still receive Hepatitis B vaccination.", result);
    }

    [Fact]
    public void IndicationGuidance_IncludedOnlyWhenPatientHasMatchingActiveObservation()
    {
        var patientWithout = MakePatient();
        var patientWith = MakePatient(new[] { new PatientObservation { Code = "002" } });

        var resultWithout = GenerateForecastGuidance.Execute(HibRiskSeries, patientWithout, Array.Empty<AntigenContraindication>(), Array.Empty<VaccineContraindication>());
        var resultWith = GenerateForecastGuidance.Execute(HibRiskSeries, patientWith, Array.Empty<AntigenContraindication>(), Array.Empty<VaccineContraindication>());

        Assert.DoesNotContain("Vaccination 14 or more days before splenectomy is suggested.", resultWithout);
        Assert.Contains("Vaccination 14 or more days before splenectomy is suggested.", resultWith);
    }

    [Fact]
    public void AntigenLevelContraindicationGuidance_IncludedOnlyWithMatchingObservation()
    {
        var rule = MeaslesContraindications.AntigenLevel.Single(c => c.ObservationCode == "157");
        var patient = MakePatient(new[] { new PatientObservation { Code = "157" } });

        var result = GenerateForecastGuidance.Execute(HepBSeries, patient, new[] { rule }, Array.Empty<VaccineContraindication>());

        Assert.Contains(result, g => g.Contains("Live vaccines should be withheld for 2 months"));
    }

    [Fact]
    public void VaccineLevelContraindicationGuidance_IncludedOnlyWithMatchingObservation()
    {
        var rule = InfluenzaContraindications.VaccineLevel.Single(c => c.ObservationCode == "157");
        var patientWithout = MakePatient();
        var patientWith = MakePatient(new[] { new PatientObservation { Code = "157" } });

        var resultWithout = GenerateForecastGuidance.Execute(HepBSeries, patientWithout, Array.Empty<AntigenContraindication>(), new[] { rule });
        var resultWith = GenerateForecastGuidance.Execute(HepBSeries, patientWith, Array.Empty<AntigenContraindication>(), new[] { rule });

        Assert.DoesNotContain(resultWithout, g => g.Contains("solid organ transplant rejection"));
        Assert.Contains(resultWith, g => g.Contains("solid organ transplant rejection"));
    }

    [Fact]
    public void AdverseReactionAlone_DoesNotTriggerGuidance_UnlikeContraindicationApplicabilityCheck()
    {
        // FORECASTGUIDANCE-1's wording is specifically "active patient observation" - unlike
        // EvaluateContraindications' applicability check, AdverseReactions alone shouldn't
        // trigger guidance inclusion here.
        var rule = MeaslesContraindications.AntigenLevel.Single(c => c.ObservationCode == "157");
        var patient = new Patient
        {
            PatientId = "p1",
            DateOfBirth = new DateOnly(1990, 1, 1),
            AdverseReactions = new[] { new PatientObservation { Code = "157" } }
        };

        var result = GenerateForecastGuidance.Execute(HepBSeries, patient, new[] { rule }, Array.Empty<VaccineContraindication>());

        Assert.DoesNotContain(result, g => g.Contains("Live vaccines should be withheld"));
    }

    [Fact]
    public void AggregatesAllApplicableSourcesTogether()
    {
        var antigenRule = MeaslesContraindications.AntigenLevel.Single(c => c.ObservationCode == "157");
        var patient = MakePatient(new[]
        {
            new PatientObservation { Code = "002" }, // matches Hib indication
            new PatientObservation { Code = "157" }  // matches Measles antigen contraindication
        });

        var result = GenerateForecastGuidance.Execute(HibRiskSeries, patient, new[] { antigenRule }, Array.Empty<VaccineContraindication>());

        // Series' own regimen guidance (Hib has none for this series, so just confirm both
        // indication + contraindication guidance made it in together).
        Assert.Contains("Vaccination 14 or more days before splenectomy is suggested.", result);
        Assert.Contains(result, g => g.Contains("Live vaccines should be withheld"));
    }
}
