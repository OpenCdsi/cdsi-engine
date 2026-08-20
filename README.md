# CDSi Immunization Engine

A clinical decision support engine implementing the CDC's CDSi Logic Specification (v4.6)
for immunization forecasting. Built as a real-time API target for a small single-clinic
EHR integration — top priority is easy updates when CDC schedule/logic changes, so the
supporting data (30 antigen XML files + the Schedule file) is treated as external, hot-swappable
data, not compiled into the application.

## ✅ Build verified

`dotnet build` and `dotnet test` have been run against this code (not just written blind) —
30/30 tests passing, 0 warnings, 0 errors, as of the Organize Immunization History and Create
Relevant Patient Series modules. Chapter 6 onward has not been written yet; see Status below.

## Status

| Pipeline stage | Spec section | Status |
|---|---|---|
| Organize Immunization History | §4.2 | ✅ Implemented + tested (incl. age-gated CVX 121 Zoster/Varicella fix) |
| Create Relevant Patient Series | §5.1 | ✅ Implemented + tested (gender + risk-indication filtering) |
| Evaluate Vaccine Dose Administered | §6.1–6.10 | ⏳ Not started — models for Age/Interval rules exist, engine doesn't |
| Forecast | §7 | ⏳ Not started |
| Select Best Patient Series | §8 | ⏳ Not started |
| Vaccine Group Merge | §9 | ⏳ Not started |

## Gotchas found while building this — expect more of these in Chapter 6

The real CDSi supporting data repeatedly uses an **empty/self-closing XML element to mean
something specific**, not "value omitted." Every parsing bug hit so far has been this same
pattern in a different place. Worth having a mental checklist for it before writing the
Chapter 6 loaders:

- `<requiredGender/>` (empty) on a series means "no restriction — applies to every gender"
  (Table 5-2: assumed value if empty is "gender of the patient"), **not** "applies to no one."
  An empty list must short-circuit to `true` in `AppliesToGender`, not `Contains()` on an
  empty collection.
- `<interval/>` / `<allowableInterval/>` (empty, no child elements) on a `seriesDose` means
  "this attribute doesn't apply to this dose" — most commonly Dose 1, which has no previous
  dose to measure an interval from. 107 empty `<interval/>` and 465 empty
  `<allowableInterval/>` placeholders exist across the 30 files. These must be filtered out
  *before* attempting to parse them as structured rules, or the parser throws on a dose that's
  actually just fine.
- Conversely, `<age>` is **never** a bare self-closing placeholder in the current data — its
  seven sub-fields are always present (even if individually empty). Don't assume every
  "container" element follows the same emptiness convention; check each one against the real
  data rather than pattern-matching from a sibling.
- Gender restriction is genuinely per-series, not per-antigen — HPV has both
  `Female`/`Unknown`-restricted series *and* separate `Standard`-type `HPV male N-dose series`
  entries. "Is antigen X ever relevant for gender Y" is not the same question as "is this
  specific series relevant for gender Y" — test the latter, not the former.

**Practical takeaway for Chapter 6**: before writing a loader for a new element type (Vaccine
Conflict, Conditional Skip, Recurring Dose, etc.), grep the real data for every self-closing
variant of that element across all 30 files first, the way we did for `<interval/>` above,
rather than inferring emptiness behavior from a handful of populated samples. It's cheap to
check and has caught a real bug every single time so far.

## Repo layout

```
src/Cdsi.Core/
  Common/           Shared utilities: DurationExpression parser, TemporalRuleSelector (§3.3)
  Models/           Patient, VaccineDoseAdministered, AntigenAdministered, Gender
  ReferenceData/     AntigenSeries/SeriesDose/AgeRule/IntervalRule models + XML loaders,
                     CvxToAntigenMap (incl. Zoster fix), VaccineConflictRule
  Pipeline/          OrganizeImmunizationHistory (§4.2), CreateRelevantPatientSeries (§5.1)
tests/Cdsi.Core.Tests/
                     xUnit tests wired to the real bundled XML fixtures (not mocks)
data/
  antigens/          All 30 CDC AntigenSupportingData-*.xml files + XSD
  schedule/          ScheduleSupportingData.xml + XSD (CVX-to-antigen map, vaccine conflicts)
```

## Build & test

```bash
dotnet restore
dotnet build
dotnet test
```

Test fixtures are copied from `data/` into the test output directory at build time (see the
`<None Include=...>` items in `Cdsi.Core.Tests.csproj`) — no manual setup needed.

## Design notes worth knowing before you extend this

- **`TemporalRuleSelector`** (`Common/`) is the one place the §3.3 "select the applicable
  time-boxed instance for an anchor date" logic lives. `AgeRule`, `PreferableIntervalRule`,
  and `AllowableIntervalRule` all implement `ITemporallyVersioned` so the Chapter 6 engine
  (when built) can reuse it rather than reimplementing the effective/cessation window check.
- **`CvxAssociation.AppliesAt`** is deliberately a *separate* check from `ITemporallyVersioned`,
  even though both are "anchor date + range" patterns — one is a calendar-date window selecting
  between rule *versions*, the other is an age-since-birth window selecting between *antigens*
  for the same CVX. Forcing them under one interface would be a false unification.
- **`Patient.UnresolvedObservationCodes`** is an explicit modeling choice (documented in code)
  for the Yes/No/Unknown distinction §5.1 requires for risk-indication matching. Worth
  confirming this matches what your actual EHR feed can tell you before this reaches real
  patients — see the XML doc comment on that property.
- **Never delete superseded reference data** when the CDC ships an update. Age/Interval/
  ConditionalSkip rules are versioned by effective/cessation date specifically so a dose given
  years ago can still be evaluated against the rule that applied *then*.
- Reference data should be mounted as a volume in the eventual Docker deployment
  (`data/` → e.g. `/data`), not baked into the image — that's the whole point of the
  data-driven design given the "easy updates" priority.

## Next steps

1. §6.1–6.10 evaluation engine (Age, Interval, Vaccine Conflict, Satisfy Target Dose aggregator) —
   next up, paused until time is available; build incrementally with tests per logical
   component (Age alone first, or Age+Interval together), rather than all 10 at once.
2. §7 Forecast.
3. §8–9 Best Series / Vaccine Group selection.
4. `Cdsi.Api` (ASP.NET) + real Dockerfile target once the pipeline is complete.
