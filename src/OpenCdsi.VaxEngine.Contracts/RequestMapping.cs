/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;

namespace OpenCdsi.VaxEngine.Contracts;

/// <summary>Maps the wire-format request DTO to OpenCdsi.VaxEngine.Core's domain models. Kept as one small, testable static class rather than inlined into the endpoint handler, so the mapping itself can be unit tested without spinning up a WebApplicationFactory.</summary>
public static class RequestMapping
{
    public static Patient ToPatient(ForecastRequestDto request) => new()
    {
        PatientId = request.PatientId,
        DateOfBirth = request.DateOfBirth,
        Gender = ParseGender(request.Gender),
        CountryOfBirth = request.CountryOfBirth,
        ActiveObservations = (request.ActiveObservations ?? Array.Empty<PatientObservationDto>())
            .Select(ToPatientObservation).ToArray(),
        AdverseReactions = (request.AdverseReactions ?? Array.Empty<PatientObservationDto>())
            .Select(ToPatientObservation).ToArray(),
        UnresolvedObservationCodes = request.UnresolvedObservationCodes ?? Array.Empty<string>()
    };

    public static IReadOnlyList<VaccineDoseAdministered> ToAdministeredDoses(ForecastRequestDto request) =>
        (request.AdministeredDoses ?? Array.Empty<AdministeredDoseDto>())
            .Select(d => new VaccineDoseAdministered
            {
                DoseId = d.DoseId,
                Cvx = d.Cvx,
                DateAdministered = d.DateAdministered,
                TradeName = d.TradeName,
                Volume = d.Volume,
                LotExpirationDate = d.LotExpirationDate,
                DoseConditionFlag = d.DoseConditionFlag
            })
            .ToArray();

    public static DateOnly ResolveAssessmentDate(ForecastRequestDto request, DateOnly today) =>
        request.AssessmentDate ?? today;

    private static PatientObservation ToPatientObservation(PatientObservationDto dto) => new()
    {
        Code = dto.Code,
        Text = dto.Text,
        ObservationDate = dto.ObservationDate
    };

    private static Gender ParseGender(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Gender.Unknown;
        }
        return value.Trim().ToLowerInvariant() switch
        {
            "male" => Gender.Male,
            "female" => Gender.Female,
            "unknown" => Gender.Unknown,
            _ => throw new InvalidRequestException($"Unrecognized gender '{value}' - expected one of: Male, Female, Unknown.")
        };
    }
}
