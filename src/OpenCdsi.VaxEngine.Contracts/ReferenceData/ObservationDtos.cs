/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace OpenCdsi.VaxEngine.Contracts.ReferenceData;

public sealed class ObservationSummaryDto
{
    public required string ObservationCode { get; init; }
    public required string ObservationTitle { get; init; }
}

public sealed class ObservationDto
{
    public required string ObservationCode { get; init; }
    public required string ObservationTitle { get; init; }
    public string? Group { get; init; }
    public string? IndicationText { get; init; }
    public string? ContraindicationText { get; init; }
    public string? ClarifyingText { get; init; }
    public required IReadOnlyList<CodedValueDto> CodedValues { get; init; }
}

public sealed class CodedValueDto
{
    public required string Code { get; init; }
    public required string CodeSystem { get; init; }
    public string? Text { get; init; }
}
