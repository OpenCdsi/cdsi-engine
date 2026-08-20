using Cdsi.Core.Models;
using Cdsi.Core.Pipeline;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class OrganizeImmunizationHistoryTests
{
    private static readonly ScheduleSupportingData Schedule =
        ScheduleSupportingDataLoader.LoadFile(TestPaths.ScheduleFilePath);

    private static Patient MakePatient(DateOnly dob) => new()
    {
        PatientId = "test-patient",
        DateOfBirth = dob
    };

    [Fact]
    public void UnconditionalCombinationVaccine_FansOutToAllAssociatedAntigens()
    {
        // Real data: CVX 20 (DTaP) -> Diphtheria, Tetanus, Pertussis, unconditionally.
        var patient = MakePatient(new DateOnly(2020, 1, 1));
        var dose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "20", DateAdministered = new DateOnly(2020, 3, 1) };

        var result = OrganizeImmunizationHistory.Execute(patient, new[] { dose }, Schedule.CvxToAntigen);

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "Diphtheria", "Pertussis", "Tetanus" }, result.Select(r => r.Antigen).OrderBy(a => a));
        Assert.All(result, r => Assert.Equal(new DateOnly(2020, 3, 1), r.DateAdministered));
    }

    [Fact]
    public void Cvx121_GivenBeforeAge50_ClassifiesAsVaricella()
    {
        // The Zoster/Varicella age-gated case (§4.2 implementer note), real data: CVX 121.
        var dob = new DateOnly(1975, 6, 1);
        var patient = MakePatient(dob);
        var dose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "121", DateAdministered = new DateOnly(2024, 1, 15) }; // age ~48.6

        var result = OrganizeImmunizationHistory.Execute(patient, new[] { dose }, Schedule.CvxToAntigen);

        var record = Assert.Single(result);
        Assert.Equal("Varicella", record.Antigen);
    }

    [Fact]
    public void Cvx121_GivenAtOrAfterAge50_ClassifiesAsZoster()
    {
        var dob = new DateOnly(1975, 6, 1);
        var patient = MakePatient(dob);
        var dose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "121", DateAdministered = new DateOnly(2027, 1, 15) }; // age ~51.6

        var result = OrganizeImmunizationHistory.Execute(patient, new[] { dose }, Schedule.CvxToAntigen);

        var record = Assert.Single(result);
        Assert.Equal("Zoster", record.Antigen);
    }

    [Fact]
    public void Cvx121_GivenExactlyAtAge50Boundary_ClassifiesAsZoster()
    {
        // associationBeginAge/EndAge use the same "begin <= x < end" convention as everywhere
        // else in the spec — the boundary itself belongs to the older-age association.
        var dob = new DateOnly(1975, 6, 1);
        var patient = MakePatient(dob);
        var dose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "121", DateAdministered = dob.AddYears(50) };

        var result = OrganizeImmunizationHistory.Execute(patient, new[] { dose }, Schedule.CvxToAntigen);

        var record = Assert.Single(result);
        Assert.Equal("Zoster", record.Antigen);
    }

    [Fact]
    public void UnmappedCvx_ProducesNoAntigenRecords()
    {
        var patient = MakePatient(new DateOnly(2020, 1, 1));
        var dose = new VaccineDoseAdministered { DoseId = "d1", Cvx = "99999-not-a-real-cvx", DateAdministered = new DateOnly(2020, 3, 1) };

        var result = OrganizeImmunizationHistory.Execute(patient, new[] { dose }, Schedule.CvxToAntigen);

        Assert.Empty(result);
    }

    [Fact]
    public void MultipleDoses_AreSortedByAntigenThenDate()
    {
        var patient = MakePatient(new DateOnly(2020, 1, 1));
        var doses = new[]
        {
            new VaccineDoseAdministered { DoseId = "d2", Cvx = "03", DateAdministered = new DateOnly(2021, 1, 1) }, // MMR
            new VaccineDoseAdministered { DoseId = "d1", Cvx = "20", DateAdministered = new DateOnly(2020, 3, 1) }, // DTaP
        };

        var result = OrganizeImmunizationHistory.Execute(patient, doses, Schedule.CvxToAntigen);

        // Diphtheria, Measles, Mumps, Pertussis, Rubella, Tetanus - alphabetical antigen order
        Assert.Equal(new[] { "Diphtheria", "Measles", "Mumps", "Pertussis", "Rubella", "Tetanus" },
            result.Select(r => r.Antigen));
    }
}
