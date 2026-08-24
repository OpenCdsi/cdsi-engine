# CDSi Immunization Engine

A clinical decision support engine implementing the CDC's CDSi Logic Specification (v4.6)
for immunization forecasting. Built as a real-time API target for a small single-clinic
EHR integration — top priority is easy updates when CDC schedule/logic changes, so the
supporting data (30 antigen XML files + the Schedule file) is treated as external, hot-swappable
data, not compiled into the application.

## License

Licensed under the Mozilla Public License 2.0 (MPL-2.0) — see the [LICENSE](LICENSE) file for
the full text and copyright notice. Every `.cs` file carries the standard MPL 2.0 file-level
notice as its first lines:

```
/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */
```

**Any new source file added to this project must carry this same header** as its first lines,
before any `using` statements or namespace declaration.

**This license covers this project's own source code only — `src/` and `tests/`.** The `data/`
directory (all 30 antigen XML files, the Schedule XML file, and both XSDs) is the CDC's own
published CDSi Logic Specification supporting data. It is not authored by this project, carries
no MPL notice, and is explicitly excluded from the MPL 2.0 license above — see
[`data/NOTICE`](data/NOTICE) for its own provenance and status. Works of the U.S. federal
government are generally not subject to copyright protection domestically under 17 U.S.C. § 105,
but this is general information, not legal advice — confirm the data's actual usage terms with
the CDC or your own counsel before relying on that for any specific distribution.

## ✅ Build verified

`dotnet build` and `dotnet test` have been run against this code (not just written blind) —
30/30 tests passing, 0 warnings, 0 errors, as of the Organize Immunization History and Create
Relevant Patient Series modules. Chapter 6 onward has not been written yet; see Status below.

## Status

| Pipeline stage | Spec section | Status |
|---|---|---|
| Organize Immunization History | §4.2 | ✅ Implemented + tested (incl. age-gated CVX 121 Zoster/Varicella fix) |
| Create Relevant Patient Series | §5.1 | ✅ Implemented + tested (gender + risk-indication filtering) |
| Evaluate Vaccine Dose Administered | §6.1–6.10 | ✅ All 10 logical components implemented + tested |
| Evaluate Immunization History (§4.4 orchestrator) | §4.4 | ✅ Implemented + tested — the two-pointer target-dose/administered-dose walk, wiring all 10 Ch.6 components together with real (not caller-supplied) Interval and Vaccine Conflict resolution. Now runs across every relevant series for a patient (`EvaluatePatientSeriesHistory`), with real cross-antigen Vaccine Conflict resolution proven end-to-end. §6.2's Completed Series condition and Recurring Dose (§4.4 step 5) are both now resolved for real too (see "Filling the gaps") |
| Forecast | §7 | ✅ Complete — all of §7.1-§7.6 implemented + tested (§7.1 Conditional Skip Forecast context, §7.2 Evidence of Immunity, §7.3 Contraindications, §7.4 Forecast Need, §7.5 Generate Forecast Dates incl. recommended vaccine/admin guidance/dose number, §7.6 Validate Recommendation) |
| Select Best Patient Series | §8 | ✅ Complete — all of §8.1-8.8 implemented + tested (Pre-Filter, Identify One Prioritized, Classify Scorable, all three point-scoring tables, Select Prioritized, Determine Best) |
| Vaccine Group Merge | §9 | ✅ Complete — all of §9.1-9.3's business rules implemented + tested, including FORECASTVG-1 (containment), FORECASTVG-8 (recommended antigen), and FORECASTVG-9 (recommended vaccine aggregation) |
| **End-to-end pipeline** | — | ✅ **Complete** — `GeneratePatientForecast` wires §4.2/§5.1 → §4.4/§6 → §7 → §8 → §9 into one call: raw administered doses in, merged vaccine group forecasts out. See "The pipeline is complete" below |

## The orchestrator (§4.4) — what it unlocked, and what's still deferred

`EvaluateSeriesHistory` implements Figure 4-6's exact 7-step algorithm: two pointers, one over
target doses and one over antigen-administered records, each advancing according to precise
rules the spec actually specifies (get the full text of §4.4 before touching this code if you
need to modify it — the pointer-advancement logic has real edge cases, see below).

**A correction to the mental model this project had built up over the last several rounds**:
re-reading Table 6-31's exact condition list while wiring components together revealed that
§6.3 Inadvertent Vaccine is NOT one of its AND-able inputs — it's a third short-circuiting gate
alongside §6.1 (Sub-standard) and §6.2 (Skipped), not a peer condition like Age/Interval/
Conflict/Vaccine. The real per-pairing order is: §6.1 gate → §6.2 gate → §6.3 gate → §6.4-6.9
(the true AND-able conditions) → §6.10 aggregates only those six. This only became visible by
actually trying to chain the pieces together — a good argument for building the orchestrator
sooner rather than treating "wire it all up" as a trivial last step.

**This is also what finally unlocked real reference-date resolution** for Interval (§6.5/6.6,
CALCDTINT-1/2) and real prior-dose data for Vaccine Conflict (§6.7) — both were caller-supplied
placeholders until now specifically because they needed chronological evaluation history that
didn't exist before this piece. Note the two different scopes: Interval and Conditional Skip
use only THIS antigen's own prior doses (`priorDosesOfThisAntigen`), but Vaccine Conflict needs
the patient's FULL cross-antigen history (`priorDosesAllAntigens`), since conflicting pairs are
frequently different antigens entirely (MMR vs Varicella, for instance).

**One thing NOT spec-grounded, explicitly flagged in code comments rather than silently
guessed** (Recurring Dose, the other item that used to be listed here, is now implemented for
real — see "Filling the gaps" for the full story):
- **"Skipped" pointer-advancement is an inference, not a spec-grounded rule.** §4.4's own 7-step
  algorithm text only discusses "Satisfied" vs "Not Satisfied" — it doesn't address Table 6-11's
  "Skipped" status at all (the two sections may have been written independently). This codebase
  advances the target-dose pointer on Skipped WITHOUT consuming the administered-dose pointer
  (the record remains available for the next target dose), which matches the overall framing but
  is this project's own design decision, not a quoted rule. Worth a second look if you ever find
  spec text that addresses this directly.

A capstone test (`EvaluateSeriesHistoryTests`) runs three real HepB doses through the complete
pipeline — `OrganizeImmunizationHistory` → `EvaluateSeriesHistory` — end to end, plus an
extra-dose-becomes-Extraneous case and a too-young-dose-blocks-advancement case.

## The patient-level orchestrator (`EvaluatePatientSeriesHistory`)

`EvaluateSeriesHistory` evaluates one series in isolation. `EvaluatePatientSeriesHistory` is the
layer above it — §4.4's Figure 4-5 high-level loop, running every relevant series for a patient
and accumulating cross-antigen history along the way. This is the piece that makes Vaccine
Conflict's cross-antigen resolution actually meaningful in practice (a single-series test can't
prove it — the conflicting dose lives in a *different* antigen's history), and it's also the
direct on-ramp to §7 Forecast: its output per series (particularly `CurrentTargetDoseNumber`) is
exactly what Forecast needs to know what to forecast next.

Proven end-to-end with real data: MMR administered 10 days before a Varicella dose correctly
gets that Varicella dose flagged `NotSatisfied`/`NotValid`/"Impacted by vaccine conflict" —
using only the *cross-antigen* history this orchestrator provides (the Varicella series has no
antigen-administered records of its own for Measles/Mumps/Rubella; the MMR shot only shows up
via the shared patient-wide history this layer builds).

Two more things worth knowing:
- **§4.4's own text is directly grounded here**: "An administered dose that is 'valid' for one
  relevant patient series may be 'not valid' for a different relevant patient series for the
  same patient." Each series evaluates completely independently against the same raw
  antigen-administered records — proven with a test running the same two real HepB doses
  against two different real HepB series.
- **"Completed Series" (§6.2's Table 6-7) is now resolved for real** — see "Filling the gaps"
  below for the full story. It turned out NOT to need §8 after all; the earlier note here
  (assuming it did) was itself a correction worth remembering, since it was wrong for a
  different, more specific reason than originally thought.

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
- Duration expressions (`absMinAge`, `minAge`, `maxAge`, interval fields, CVX-map association
  ages, conflict intervals) aren't always "N unit - N unit" — Rotavirus's `maxAge` uses
  **"8 months + 1 day"**. `DurationExpression` originally only parsed the subtraction form and
  threw on the first `+` it hit. Swept every duration-bearing field across all 30 files plus
  the Schedule file after fixing it (`+`/`-` both handled now) and confirmed no other operator
  or multi-adjustment shape exists in the current data — but that sweep is worth re-running
  against any future CDC data drop, not just trusted once.

## Historical scope gap — now resolved by the orchestrator

`EvaluatePreferableInterval`/`EvaluateAllowableInterval` and `SatisfyTargetDose` were originally
built taking already-resolved values (a reference date; conflict/vaccine booleans) as parameters
rather than computing them, because the chronological history needed to resolve them for real
didn't exist yet. `EvaluateDoseAgainstTargetDose` (built alongside the §4.4 orchestrator) now
supplies the real thing at every one of those call sites — see "The orchestrator" section above
for exactly how. This section is kept for the historical reasoning (the individual component
tests still exercise them with hand-supplied resolvers/booleans in isolation, which remains
useful for testing each component's own decision logic independent of the full pipeline).


Note that §6.7 and §6.8/6.9 turned out to be **more self-contained than Interval**: none of them
need "which dose satisfied target dose N," which is what made Interval's resolution genuinely
dependent on the not-yet-built orchestrator. So all three implement their real business rules
end-to-end today. Worth keeping in mind when scoping the remaining components: not every
"depends on prior dose state" requirement is actually blocked on the orchestrator — check what
the business rule literally needs before assuming it is.

One extra model gap §6.8 surfaced: `VaccineDoseAdministered` never captured trade name or
volume, because nothing needed them until Table 6-26's preferable-vaccine comparison required
both. Added as nullable fields — worth double-checking your real EHR feed can actually supply
these when the API layer gets built, since they're absent from ~98.5% of real preferableVaccine
entries (so usually unnecessary) but required for the ~1.5% that do specify a trade name.

## §6.1 and §6.2: two more evaluation statuses, and the messiest data yet

§6.1 (Table 6-3) introduced a fourth evaluation status, "Sub-standard," and §6.2 (Table 6-11)
introduces a fifth, "Skipped" — neither fits the Valid/Not Valid/Extraneous vocabulary
`DoseEvaluationOutcome` was built around. Rather than force them in, both got their own small,
dedicated result types (`DoseAdministeredConditionResult`, and a plain `bool` for
`EvaluateConditionalSkip`). Unifying all five possible final statuses into one type is
deliberately left to the orchestrator, once it's clear how they actually need to compose —
guessing at that shape now, before the orchestrator exists, seemed more likely to be wrong than
useful.

§6.2's real data is also the messiest encountered in this project: `conditionType` values are
inconsistently cased across files (`"Vaccine Count by Age"` vs `"Vaccine Count By Age"`),
`doseType` mixes `"Valid"` and `"valid"`, and `doseCountLogic` mixes `"greater than"` and
`"Greater Than"`. Every enum parse in the conditionalSkip loader is deliberately
case-insensitive as a result — confirmed against a full sweep of all 264 real conditionalSkip
instances (469 conditions) before any evaluator code was written, not after a test failure.

Two things about §6.2 worth flagging as inferences rather than spec-grounded facts:
- The spec doesn't say how **multiple top-level `conditionalSkip` instances** on the same dose
  combine (only how Sets and Conditions combine *within* one instance). Real data has up to 2
  per dose. This codebase ORs them — a dose is skippable if *any* instance says so — which
  matches the overall framing ("a dose is skippable only when explicitly determined to be"),
  but it's an inference, not a quoted rule.
- Only conditionalSkip instances with `context` "Evaluation" or "Both" are loaded at all (the
  spec is explicit about this) — "Forecast"-only instances are silently dropped, which is
  correct for Chapter 6 but means this loader isn't reusable as-is for a future Chapter 7
  Forecast implementation without changing that filter.

"Completed Series" (Table 6-7) was, at this point in the project, still caller-supplied — it
needed another relevant patient series' completion status, which didn't exist anywhere in this
codebase yet (no series-level status tracking had been built at the time). **Now resolved for
real — see "Filling the gaps" for the full story; it turned out to need only §4.4's own
`SeriesHistoryResult.SeriesComplete`, not Chapter 8 as originally guessed.** Everything else in
§6.2 — Age, Interval, and Vaccine Count (which covers all the "by Age"/"By
Date"/"by Date and Age" casing variants under one unified type, since CONDSKIP-1's counting rule
always applies both an age and a date filter regardless of which the data author intended to be
meaningful) — is fully implemented against real business rules.

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
  Evaluation/        Chapter 6 logical components — all 10 implemented — plus all of Chapter 7
                     Forecast: EvaluateEvidenceOfImmunity (§7.2), EvaluateContraindications
                     (§7.3), DetermineForecastNeed (§7.4), GenerateForecastDates +
                     ForecastIntervalDates (§7.5, core date calc), DetermineRecommendedVaccine
                     (§7.5, FORECASTRECVAC-1), DetermineForecastDoseNumber (§7.5,
                     FORECASTDN-1), GenerateForecastGuidance (§7.5, FORECASTGUIDANCE-1),
                     ValidateRecommendation (§7.6). Chapter 8: PreFilterPatientSeries (§8.1),
                     IdentifyOnePrioritizedPatientSeries (§8.2),
                     ClassifyScorablePatientSeries (SELECTB-6/16/21 + §8.3 Table 8-5),
                     ScoreCompletePatientSeries (§8.4), ForecastFinishDate (§8.5, SELECTB-12),
                     ScoreInProcessPatientSeries (§8.5, Table 8-9),
                     ScoreNoValidDosesPatientSeries (§8.6, Table 8-11),
                     SelectPrioritizedPatientSeries (§8.7), DetermineBestPatientSeries (§8.8).
                     Chapter 9: VaccineGroupClassification (§9.1, VACCINEGROUP-1/2),
                     VaccineGroupForecastDates (§9.1, FORECASTVG-2..6/FORECASTDN-2),
                     SingleAntigenVaccineGroup (§9.2),
                     MultipleAntigenVaccineGroup (§9.3, Table 9-4/MULTIANTVG-1/FORECASTPRIORITY-1),
                     VaccineGroupForecastAggregation (§9.1, FORECASTVG-1/8/9),
                     ForecastConflictEndDate (CALCDTCONFLICT-3, forward-looking conflict
                     resolution for §7.5's FORECASTDTCAN-1).
                     DoseEvaluationOutcome (shared result type for §6.4-6.9),
                     EvaluateDoseAdministeredCondition (§6.1), EvaluateConditionalSkip
                     (§6.2/§7.1, context-aware), EvaluateInadvertentVaccine (§6.3),
                     EvaluateAge (§6.4), EvaluatePreferableInterval (§6.5),
                     EvaluateAllowableInterval (§6.6), EvaluateVaccineConflict (§6.7),
                     EvaluatePreferableVaccine (§6.8), EvaluateAllowableVaccine (§6.9),
                     SatisfyTargetDose (§6.10 aggregator), EvaluateDoseAgainstTargetDose
                     (wires 6.1-6.10 together).
  Pipeline/          OrganizeImmunizationHistory (§4.2), CreateRelevantPatientSeries (§5.1),
                     EvaluateSeriesHistory (§4.4 per-series orchestrator),
                     EvaluatePatientSeriesHistory (§4.4 per-patient orchestrator),
                     GeneratePatientSeriesForecast (§7 per-series forecast orchestrator),
                     SelectPrioritizedPatientSeriesForGroup (§8.1-§8.7 per-series-group
                     orchestrator), DetermineBestPatientSeriesForAntigen (§8.8 per-antigen
                     orchestrator), MergeVaccineGroupForecast (§9 per-vaccine-group merge),
                     GeneratePatientForecast (the complete end-to-end pipeline — raw doses in,
                     merged vaccine group forecasts out), ResolveCompletedSeriesGroups
                     (§6.2's Completed Series condition, resolved via a two-pass approach)
tests/Cdsi.Core.Tests/
                     xUnit tests wired to the real bundled XML fixtures (not mocks)
src/Cdsi.Demo/
                     Console app: loads the FULL real 30-antigen catalog via
                     ReferenceDataRepository and runs a few sample patients through
                     GeneratePatientForecast end to end, printing real forecast output.
                     `dotnet run --project src/Cdsi.Demo` from the repo root.
src/Cdsi.Api/
                     Minimal-API ASP.NET Core 8 web service wrapping GeneratePatientForecast.
                     Contracts/ holds the request/response DTOs and their mapping to/from
                     Cdsi.Core's domain models. `dotnet run --project src/Cdsi.Api`, or
                     `docker compose up --build` from the repo root — see "Cdsi.Api — the
                     dockerized web API" below.
tests/Cdsi.Api.Tests/
                     Real HTTP integration tests via WebApplicationFactory<Program> - the
                     actual Program.cs startup running in-memory against the real data/
                     directory, not mocked.
data/
  antigens/          All 30 CDC AntigenSupportingData-*.xml files + XSD
  schedule/          ScheduleSupportingData.xml + XSD (CVX-to-antigen map, vaccine conflicts)
Dockerfile           Multi-stage build for Cdsi.Api - see "Cdsi.Api — the dockerized web API"
docker-compose.yml   Builds and runs the API locally with the data/ volume mounted
```

## Build & test

```bash
dotnet restore
dotnet build
dotnet test
```

Test fixtures are copied from `data/` into the test output directory at build time (see the
`<None Include=...>` items in `Cdsi.Core.Tests.csproj`) — no manual setup needed.

## Run the whole pipeline yourself

```bash
dotnet run --project src/Cdsi.Demo
```

Loads all 30 real antigens + the schedule via `ReferenceDataRepository` (now extended to also
load immunity/contraindication data and vaccine groups — see below), then runs three sample
patients — a newborn with no doses, a 2-month-old with just the HepB birth dose, and a
15-month-old partway through the routine schedule — through `GeneratePatientForecast`,
printing each vaccine group's status and forecast dates as real output you can read.

**Couldn't be run or verified from this environment** — this sandbox has no `dotnet` runtime, so
this hasn't been executed or checked against real output the way `dotnet test` results have been
throughout this project. Every function it calls into has its own tests that do pass, and the
wiring was checked field-by-field against each type's real definition before being written, but
this specific combination — the full 30-antigen catalog, these specific sample patients — is
genuinely unverified. If something looks off when you run it, that's real signal, not noise.

`ReferenceDataRepository` was extended this round (backward compatible — existing `.AllSeries`/
`.Schedule` usage is untouched) to also load `ImmunityByAntigen`, `ContraindicationsByAntigen`,
and `VaccineGroups`, since the full pipeline needs all of that and the repository was the natural
single place to build it once.

### A real crash, found only by actually running the full catalog

The very first run — a newborn, zero doses — crashed: `SingleAntigenVaccineGroup.Status` threw
on a single antigen with two contained "best patient series" (§8.8) reporting genuinely
*different* statuses (`NotRecommended` and `NotComplete`), not just redundant agreement. This
is exactly the kind of thing that can only surface by actually running the system against real
data at scale — no amount of unit testing individual pieces in isolation would have found it,
since it depends on real reference data producing two disagreeing "best" series for the same
antigen simultaneously.

The fix required real judgment, not just a broader tolerance check. My first attempt at this
(a few rounds ago) only handled *agreement* among multiple contained statuses, and threw on any
disagreement, reasoning that disagreement must be a data inconsistency. That reasoning was
wrong: multiple series *groups* for one antigen are alternative paths to protecting that
antigen, not independent requirements the way multiple *antigens* in a multi-antigen vaccine
group are. `MultipleAntigenVaccineGroup`'s "worst status dominates" cascade is correct for the
latter (every antigen must be addressed) but wrong for the former — reporting `NotRecommended`
because one alternative, non-chosen path happened to have nothing due right now would silently
hide a real, actionable `NotComplete` recommendation via a different path. The fix: if *any*
contained status is `NotComplete`, that wins outright; only when nothing is actionable does it
fall back to the multi-antigen cascade's worst-case ordering, since at that point there's no
recommendation left to hide.

Also cleaned up an editing mistake caught immediately afterward: the first fix attempt left
dead, unreachable code and a mismatched brace behind from an incomplete edit — caught by
re-viewing the whole method before considering the fix done, not left for the next compile to
surface.

Existing tests updated: the old "genuinely conflicting statuses throws" test is gone (that
behavior was wrong and has been replaced), with a new test built directly from the real crash
scenario (`NotRecommended` + `NotComplete` → `NotComplete`) plus a worst-case-fallback test for
when nothing is actionable.

### A second real finding — RSV's 2101 date, and why it wasn't a bug

The crash fix above got the pipeline running, but the newborn scenario's RSV result still
looked wrong: `earliest 2101-08-23` — 75 years out. Real RSV data has two `Standard`-type series
(infant, 0 days–8 months, and 75-and-older), and hand-tracing every layer of §5.1/§8.1/§8.7/§8.8's
actual source code — three separate times — said both should become "best" and merge via `Min()`,
with the infant's near-term date winning. Static reading alone couldn't find where that broke,
so temporary diagnostic output was added to `Cdsi.Demo` to print the pipeline's real intermediate
state, since only actual execution (which this sandbox can't do) could answer it.

The diagnostic's answer: §8.8 was working correctly — both series genuinely became "best." The
infant series' own forecast *status* was `NotRecommended`, not the assumed `NotComplete`, so §9's
merge correctly excluded it from date math (a non-forecasting series contributes nothing, by
design). The real question became: why `NotRecommended`? Checking RSV's actual XML data answered
it — the infant series has a real, on-file seasonal window, `2025-10-01` to `2026-03-31`. The
demo's assessment date (August 2026) was past that window's end, so `DetermineForecastNeed`
correctly applied Table 7-10's seasonal gate. Real Influenza data showed the identical pattern in
the identical run (`NotRecommended`, same reason) — which had been sitting in the output the
whole time as a second, unremarked confirmation of the same real behavior.

**This was not a code defect.** Every layer computed correctly; picking an off-season assessment
date for a vaccine with a wide infant/older-adult population split produced a technically-correct
but confusing-looking result, since the *only* contributing series to the merge happened to be
the wrong population's. Fixed by picking a demo assessment date (`2026-01-15`) that falls inside
both RSV's and Influenza's real on-file seasonal windows, and by removing the diagnostic block
now that it had done its job. No source logic changed as a result of this investigation — only
the demo's own chosen date.

Worth remembering for anyone reading this pipeline's output going forward: a vaccine group's
merged forecast reflects only its currently-*forecasting* contained series. If the population or
season you expected to see isn't reflected, that's a real signal to check that specific series'
own status and reason, not necessarily a pipeline bug.

### A user-requested addition: `AllPreferableVaccineCvxCodes`

Running the demo surfaced a genuine, real usability question: `RecommendedVaccineCvxCodes` was
empty for almost every antigen in the output, with Pneumococcal the lone exception. Checked
against real data before answering: `FORECASTRECVAC-1` correctly requires a vaccine's
`forecastVaccineType` flag to be `'Y'` before it counts as "recommended," and only 347 of 1089
real `preferableVaccine` entries across the whole dataset are flagged that way — confirmed
concretely for the doses this demo actually forecasts (HepB, Hib, and Polio's Dose 1 are all
`'N'` across every listed vaccine; Pneumococcal's Dose 1 happens to be the one with both entries
flagged `'Y'`). That's not a gap — CDC's own data deliberately distinguishes "safe to
auto-suggest" from "leave to clinical judgment," and forcing every dose to show a recommendation
would misrepresent that real distinction.

What *was* a genuine, worthwhile addition: `DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine`
(the literal `FORECASTRECVAC-1` rule) was refactored to delegate to a new, non-spec-named
`IsPlausibleSeriesDoseVaccine` - the same age-window/contraindication check, without the
`forecastVaccineType='Y'` gate. `PatientSeriesForecastResult` and `VaccineGroupForecastResult`
both now carry a parallel `AllPreferableVaccineCvxCodes` field alongside the existing
`RecommendedVaccineCvxCodes`, giving callers the fuller picture — "everything clinically valid
for this dose" versus "what CDC specifically flags as a default suggestion" — without changing
what "recommended" means anywhere in the existing pipeline. The refactor is behavior-preserving
for the existing function; all of its existing tests hold unchanged.

**A real bug caught before it shipped, by checking rather than assuming**: the first draft
computed per-vaccine contraindication status via `.ToDictionary(pv => pv.Cvx, ...)`, reused
across both the recommended and plausible lists. Sweeping all 30 files first (a habit that keeps
paying off) found 9 real cases where a single dose lists the *same* CVX more than once with
different age windows or trade names - Influenza's own standard series among them. That
dictionary construction would have thrown on every one of them. Fixed by keying on the vaccine
*entry* (via a list of tuples) rather than deduplicating by CVX, which both avoids the crash and
correctly preserves age-window-specific entries that happen to share a CVX.

## Filling the gaps: §6.2's Completed Series, resolved for real

Four gaps had been documented as deliberately deferred: `latestConflictEndDate`/
`latestInadvertentAdministrationDate` (§7.5), multi-antigen priority-forecast wiring (§9.3),
Recurring Dose (§4.4), and §6.2's Completed Series condition. This round closed out the last one
— and it turned out to need far less than a much earlier README note had guessed.

**The earlier guess was wrong in a specific, useful way.** A previous round's note assumed
Completed Series needed Chapter 8's "best patient series" concept, since Chapter 8 wasn't built
yet at the time. Re-reading the spec's own glossary entry precisely — "If the patient has
completed a patient series in the specified Series Group, then the condition is met" — settled
it: this condition is evaluated DURING §6's own per-dose walk, before §7 Forecast or §8 Select
Best Patient Series ever run. It can only meaningfully mean `SeriesHistoryResult.SeriesComplete`
(a pure §4.4 evaluation concept - every target dose already Satisfied or Skipped), not anything
requiring immunity, contraindication, or age gates that don't exist yet at that point in the
pipeline. Chapter 8 was never actually a prerequisite.

**Grounded against every real instance before writing anything**: swept all 30 files for
`Completed Series` conditions. Every single real one is cross-group WITHIN the same antigen -
Risk-type series (group "2") checking whether the Standard series (group "1") is already done,
e.g. "skip HepB's risk-based Dialysis series doses if the patient already completed the regular
HepB 3-dose series." Never self-referential. That's what makes a two-pass resolution converge
correctly: evaluate once assuming nothing is complete, build a lookup of which (antigen, series
group) pairs actually are, evaluate again with the real answer available. `ResolveCompletedSeriesGroups`
does exactly this, and `GeneratePatientForecast` now runs both passes internally.

**A real architectural gap, found and fixed properly rather than patched around**: the resolver
signature (`Func<string?, bool>`, just a series-group string) had no antigen context - but the
same group string ("1", "2") means something completely different per antigen file. Traced every
one of the 13 files touching this signature before changing anything. Turned out only
`EvaluatePatientSeriesHistory` (the one function that loops across MULTIPLE antigens at once)
needed a real signature change, to `Func<string, string?, bool>` - everything below it
(`EvaluateSeriesHistory`, `EvaluateDoseAgainstTargetDose`, `EvaluateConditionalSkip`,
`ValidateRecommendation`, `GeneratePatientSeriesForecast`) keeps its existing, simpler
`Func<string?, bool>` signature unchanged, since the antigen-scoping happens via a closure built
exactly once, at the one layer that actually has antigen context available. A smaller, more
surgical fix than the initial 13-file count suggested.

**A genuine API simplification, not just a gap-fill**: since Completed Series is now fully
resolvable internally, `GeneratePatientForecast.Execute` no longer takes a `resolveCompletedSeries`
parameter at all - callers (including `Cdsi.Demo`) shouldn't need to know this mechanism exists.
The demo's own conservative `_ => false` stub and its explanatory comment are gone; the real
resolver runs every time now.

Tested at three levels: `ResolveCompletedSeriesGroupsTests` (the lookup-building logic in
isolation, including that a complete group "1" for HepB correctly does NOT make Measles' group
"1" or HepB's own group "2" appear complete), a real-data test proving the exact HepB Dialysis
Dose 1 fixture flips `CanBeSkipped` based on the resolver's answer, and a genuine end-to-end
integration test running the real two-pass mechanism against real HepB data - a patient who
completes the Standard series correctly causes the Risk series' Dose 1 to become `Skipped`
rather than left `NotSatisfied`, verified against `EvaluateSeriesHistory`'s actual algorithm
(confirmed by re-reading its source that a Skipped target dose still produces a `DoseResults`
entry, unconditionally, before deciding what to assert).

Three gaps remain, each substantial enough to warrant its own dedicated round rather than being
rushed in alongside this one: `latestConflictEndDate`/`latestInadvertentAdministrationDate`
(§7.5's forward-looking conflict/inadvertent-administration calculations), multi-antigen
priority-forecast wiring (§9.3's `MULTIANTVG-1`), and Recurring Dose (§4.4).

## Filling the gaps: `latestConflictEndDate`/`latestInadvertentAdministrationDate`, resolved

Two of the three remaining gaps, closed together since they're both `FORECASTDTCAN-1` inputs.

**`latestInadvertentAdministrationDate` turned out to need no new logic at all** - §6.3's own
evaluation results already carry the exact reason string ("Inadvertent Administration") on the
`TargetDoseEvaluationResult`s already sitting in `seriesHistory.DoseResults`. This is a plain
filter-and-max extraction, confirmed by re-reading `EvaluateDoseAgainstTargetDose`'s actual
source before writing it, not assumed from memory of having built §6.3 many rounds ago.

**`latestConflictEndDate` needed a genuine new calculation** - `CALCDTCONFLICT-3`, the
forward-looking counterpart to §6.7's own `CALCDTCONFLICT-1/2`. Where those look *backward* (was
a just-administered dose too soon after a conflicting prior one?), this looks *forward*: given a
target dose's own preferable vaccines and the patient's full cross-antigen prior history, when
would an existing conflict actually clear? Reuses the exact same `VaccineConflictRule` reference
data §6.7 already loads - a different walk over it, not new reference data. One real textual
difference worth remembering: `CALCDTCONFLICT-2` branches on the prior dose's evaluation status
(minimum vs. full end interval); `CALCDTCONFLICT-3`'s own text has no such branching - it uses
the plain `ConflictEndInterval` uniformly. Implemented literally as written rather than assumed
to mirror the more elaborate neighboring rule just because they're related.

**A real, if minor, inconsistency in the spec's own documentation, worth noting rather than
"fixing"**: Table 7-9's attribute list still cites the retired `CALCDTLIVE-4` rule ID for this
exact attribute ("Latest Conflict End Interval Date"). The spec's own change log confirms
`CALCDTLIVE-4` ("This rule is no longer used") was replaced by `CALCDTCONFLICT-3` - the
attribute table was evidently never updated when the rule was renamed. Flagged in code, not
treated as this codebase's problem to reconcile.

**A real architectural question, resolved cleanly**: `GeneratePatientSeriesForecast` needed
cross-antigen prior-dose history to compute the conflict calculation, which it didn't have
access to (it only sees ONE series' own `SeriesHistoryResult`). Rather than have
`EvaluatePatientSeriesHistory` expose its internal patient-wide accumulation (a bigger API
change), `GeneratePatientForecast` reconstructs an equivalent patient-wide history at the
caller level - one antigen's worth of evaluated doses per antigen, the same "only contribute
once per antigen" simplification `EvaluatePatientSeriesHistory` already documents for its own
internal accumulation. Small, consistent, doesn't touch an already-tested function's contract.

Tested at three levels: `ForecastConflictEndDateTests` (the calculation in isolation, against
the real MMR→Varicella conflict pair already established throughout this project's §6.7 work),
a real end-to-end test proving a recent MMR dose genuinely pushes a Varicella series' own
`EarliestDate` forward from its long-past `minAgeDate` to just after the conflict clears, and a
wiring test for the inadvertent-administration extraction using a synthetic `DoseResults` entry
(real COVID-19 data has genuine `inadvertentVaccine` entries, but reconstructing a full real
dose history wasn't needed to test just this one extraction).

## Filling the gaps: multi-antigen priority forecast (§9.3's `MULTIANTVG-1`), resolved

The third of the original four gaps. `MergeVaccineGroupForecast` already had
`MultipleAntigenVaccineGroup.IsPriorityPatientSeriesForecast`/`.EarliestDate` built and tested in
isolation from the original Chapter 9 rounds - the gap was purely that nothing ever computed real
values for their two inputs, so `MULTIANTVG-1`'s "priority forecast" branch never actually fired.

**`IsPriorityForecast` is a genuinely new field**, added to `PatientSeriesForecastResult` and
computed inside `GeneratePatientSeriesForecast.Execute` by resolving one applicable
`PreferableIntervalRule` per reference-point group - reusing
`EvaluatePreferableInterval.GroupByReferencePoint` directly rather than re-implementing that
resolution a second time, so it can't quietly drift from how §7.5's own interval math already
works. Anchored to the forecast's own `EarliestDate`, the same reference point
`CalculateForecastDates` already uses for its interval lookups.

**`latestAdministeredDateOfGroupVaccineTypes` turned out to need no new data at all** -
`GeneratePatientForecast` already builds `patientWideHistoryByAntigen` (from the previous round's
conflict-date work), which is exactly antigen-keyed evaluated-dose history. For a given vaccine
group, this is just "look up every antigen the group classifies, take the latest date
administered across all of them" - no CVX-to-antigen reverse mapping needed, since that
classification already happened upstream in `OrganizeImmunizationHistory`.

Tested against real Pertussis data - genuinely part of the real multi-antigen `DTaP/Tdap/Td`
group, with a real `intervalPriority="override"` flag on its own Dose 2 interval (the same real
value this project found months ago when `FORECASTPRIORITY-1`'s spec text says "Y" but the data
never does) - proving `IsPriorityForecast` resolves `true` for a genuinely relevant real fixture,
plus a contrasting HepB test confirming it resolves `false` when no priority flag exists at all
(not just defaulting to `false` without ever computing anything).

## Filling the gaps: Recurring Dose (§4.4) — all four original gaps now closed

The last of the original four, and the one flagged from the start as the largest and riskiest,
since it meant reopening the core two-pointer evaluation loop everything else in this project
sits on top of - not sitting cleanly above already-working code the way the other three did.

**Re-reading the spec's own step 5 text precisely, rather than trusting memory of "barely more
than a one-line flag definition," changed the plan.** It's a real, if terse, algorithm step: on
satisfying a target dose flagged recurring, "initialize a new target dose identical to the
current target dose... immediately following the current target dose" and move to *that clone*
next, rather than advancing to whatever's genuinely next in the series. The insight that made
this tractable without ever mutating or growing the target-dose array: not advancing `targetIdx`
on a Satisfied recurring dose (while still advancing `adminIdx` as normal) produces an
*observationally identical* result to inserting a clone and moving to it - the same target dose,
with its own already-general interval rules (typically `fromPrevious`), gets re-evaluated against
the next administered record, using the just-updated reference date. A one-line conditional
change (skip `targetIdx++` when `IsRecurringDose`) rather than a structural rewrite.

**Grounded against every real instance before writing a line of code.** Swept all 30 files for
the `recurringDose` field (a required element on every real `seriesDose`, not optional) - 484
total, 29 flagged `"Yes"` (matching the established `"Yes"`/`"No"` convention already seen for
`defaultSeries`/`productPath`/`administerFullVaccineGroup`, not the `"Y"`/`"N"` the spec's
generic glossary describes). Every single real recurring dose is the *last* target dose in its
series - Td boosters, annual COVID/flu, occupational rabies exposure - exactly matching the
spec's own narrative examples. Confirmed real Tetanus Dose 11's actual interval data
(`fromPrevious`, `minInt` "5 years", `earliestRecInt` "10 years", no age gate) before building
the test around it, rather than assuming the numbers.

**A genuinely elegant consequence, not just a targeted fix**: because a recurring dose's
`targetIdx` never advances past it while administered records keep satisfying it, a genuinely
recurring series is now correctly *never* `SeriesComplete` - `CurrentTargetDoseNumber` stays
pinned on the recurring dose indefinitely. This isn't a special case bolted on; it falls out
naturally from the existing "series complete when `targetIdx` reaches the end" logic, which
never needed to change at all. Multiple satisfied doses legitimately sharing the same
`SatisfiedTargetDoseNumber` (each a different calendar occurrence of the same recurring
requirement) is likewise handled by logic that was already correct - `targetDoseSatisfiedDates`
already overwrites on each new satisfaction, so `fromPrevious`-style interval references
naturally re-anchor to the *latest* occurrence, not the first.

Tested against the real Tetanus Dose 10→11 fixture: a synthetic 2-dose series built from the two
real `SeriesDose` objects (matching this project's established "reuse real per-dose data in a
synthetic wrapper" pattern), Dose 10 satisfied once, then three separate Td boosters spaced 10
years apart - all four correctly `Satisfied` (the pre-fix behavior would have marked boosters 2
and 3 `Extraneous`, since `targetIdx` would have advanced out of bounds after the first one),
`SeriesComplete` staying `false` throughout, and three `EvaluatedAntigenDose` entries correctly
sharing `SatisfiedTargetDoseNumber` 11. A regression test confirms real HepB (no recurring flag)
still completes normally, unaffected by this round's change.

**All four gaps documented at the start of this phase — §6.2's Completed Series,
`latestConflictEndDate`/`latestInadvertentAdministrationDate`, multi-antigen priority forecast,
and now Recurring Dose — are closed.** Every documented, deliberately-deferred piece of this
project's core CDSi logic has been resolved, each grounded in real data before implementation,
each with tests built from real fixtures rather than synthetic approximations wherever real data
made that possible.

## The full 18-series HepB competition, run for real

The genuine "all 18 real HepB series competing at once" test this project's own README had
flagged as follow-up work since early on, once a real `dotnet` runtime was confirmed reliable.

**Honesty about what could actually be verified without execution, decided before writing a
single assertion.** This sandbox still has no `dotnet` runtime. Hand-tracing a full multi-way
§8.5 In-Process scoring competition (product-path bonus, completable, most-valid-doses,
closest-to-completion, can-finish-earliest, each needing its own `ForecastFinishDate`
projection) across several genuinely-competing real series is exactly the kind of complex,
error-prone reasoning this project has consistently avoided asserting without being able to
check it. Rather than guess and risk shipping a false assertion with no way to catch it, the two
tests here are scoped to what's actually provable by hand against the real data:

- **Zero doses, zero risk indications** — asserted down to the exact winning series
  (`"HepB 3-dose series"`), with full confidence. Its outcome turns out to be governed entirely
  by two already-independently-tested, simple mechanisms, not a scoring contest at all: all 8
  real Risk-type series get excluded from relevance (no indications, confirmed via §5.1's own
  logic), and with zero doses given, all 10 Standard series fail `SELECTSCORE-2`'s bullets 2
  *and* 3 (bullet 3 specifically needs *no* default series in the group, but `"HepB 3-dose
  series"` genuinely is one, confirmed directly against the real `defaultSeries` flag) - so
  §8.1/§8.2's own "no scorable series" fallback resolves directly to the real default. Verified
  against `PreFilterPatientSeries`'s and `SelectPrioritizedPatientSeriesForGroup`'s actual source
  before trusting the trace, not just from memory of having built them months ago.
- **Two real CVX "08" doses given** — *not* asserted down to an exact winner. What's provable by
  hand: checking all 10 Standard series' real age/CVX data directly shows exactly three
  (`"HepB 3-dose series"`, `"HepB 4-dose series"`, `"HepB Heplisav-B secondary 4-dose series"`)
  can possibly have `ValidDoseCount > 0` for these two doses - every other series either requires
  a completely different CVX (the Heplisav-B/Twinrix product lines) or a minimum age this
  2-month-old hasn't reached (11-60 years across the adolescent/19+/Twinrix variants). The test
  asserts the winner must be one of those exact three, and explicitly asserts it's none of the
  other 7 Standard series or any of the 8 Risk series - a real, meaningful proof that the
  18-series narrowing genuinely works, without overclaiming certainty about the specific
  tie-break among the three genuine candidates.

A third, small sanity-check test guards both of the above against a future CDC data update
silently changing the catalog's shape (a new series, a changed default) in a way that would make
the hand-traced assumptions stale without anything else failing to signal it.

**This sanity-check test itself was wrong on the first `dotnet test` run** - a genuinely useful
catch. It assumed exactly one `defaultSeries="Yes"` series across the whole antigen; real data
has two - `"HepB 3-dose series"` (group 1, Standard) *and* `"HepB risk Dialysis 4-dose series"`
(group 2, Risk). `defaultSeries` turns out to be unique *per series group*, not globally per
antigen - each group gets its own fallback for when `SelectPrioritizedPatientSeriesForGroup`'s
own zero-scorable-series case is reached within that specific group, which makes real structural
sense once seen. Checked whether this threatened the other two tests' own reasoning before
fixing anything: it didn't - `PreFilterPatientSeries`'s "no default in group" check (bullet 3)
was always scoped per series group already, and group 2 is excluded from relevance entirely in
both scenarios (no matching indication), so neither test's actual assertion needed to change.
Only the sanity check's own premise did - fixed to assert one default *per group*, not one
globally, with both real defaults confirmed by name.

**A real, if minor, discovery made while grounding this**: real CVX "08" doses satisfy more than
one series' early doses simultaneously by design - `"HepB 3-dose series"` and `"HepB 4-dose
series"` share the exact same Dose 1/2 age and CVX requirements, which makes sense once you
consider that CVX "08" is a generic pediatric HepB vaccine valid across multiple real-world
dosing regimens. That overlap is real-world-accurate, not a data inconsistency - but it's exactly
why a truly generic "give a common vaccine early" scenario can't be engineered to have a single,
hand-verifiable winner without deep, exhaustive verification across every remaining scoring
condition.

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

## Correction: §8 depends on §7, not the other way around

A prior version of this README suggested building §8 Select Best Patient Series before or
alongside Forecast. That was wrong, caught before any Chapter 8 code was written: §8.1/§8.2's
actual business rules (`SELECTB-24`, `SELECTSCORE-2`, `SELECTB-6`, `SELECTB-16`) all key off
**"patient series forecast"** and **"patient series status"** (`Complete`, `Not Complete`,
`Contraindicated`, etc.) — both are Chapter 7 outputs. The real dependency order is Evaluate →
Forecast → Select Best Patient Series. §6.2's "Completed Series" condition therefore has a real
circular-looking dependency (it needs §8's output, §8 needs §7's output, and §7 itself reuses
§6.2's machinery for its own conditional-skip check) — resolving that fully likely needs a
multi-pass architecture, not something to improvise mid-Forecast-build. Flagged, not solved.

**Update, once actually resolved (see "Filling the gaps")**: the "multi-pass architecture"
guess was right, but the "needs §8's output" guess was wrong — Completed Series turned out to
need only §4.4's own `SeriesHistoryResult.SeriesComplete` (a pure evaluation concept), resolved
via two passes of §4.4 alone, no Forecast or Select Best Patient Series involved at all. Worth
remembering as a reminder that even a carefully-reasoned dependency analysis can have a specific
detail wrong while still being right about the general shape of the problem.

## §7.1: reusing §6.2 required a real architecture fix, not just a new caller

§7.1 turned out to need Conditional Skip instances with `context` "Forecast"/"Both" — but the
loader had been hard-filtering to "Evaluation"/"Both" **at load time** since the very first
Conditional Skip build, discarding Forecast-only instances entirely. This was flagged as a known
limitation in an earlier README revision ("this loader isn't reusable as-is for a future Chapter
7 Forecast implementation") — and then it actually blocked real work, which is exactly what that
kind of flag is for.

Fixed properly rather than worked around: the loader now loads **all** conditionalSkip instances
regardless of context, and `EvaluateConditionalSkip.CanBeSkipped` takes a new
`ConditionalSkipContext` parameter (`Evaluation` or `Forecast`) that filters at evaluation time
instead. This touched the loader, the evaluator's public signature, its one production call site
(`EvaluateDoseAgainstTargetDose`), and all ten of its existing tests — a real, multi-file change
to already-tested code, not a one-line patch. Worth it: real Hib data has genuinely different
thresholds per context (Evaluation: `"15 months - 4 days"` with a grace period; Forecast:
`"15 months"` exactly, no grace) — proven with a test where the *same* reference date passes
under one context and fails under the other.

## §7.2 Determine Evidence of Immunity

A clean, self-contained piece: a patient is presumed immune either via a documented clinical
finding, or via a "born before a defined date" presumption that can itself be overridden by an
exclusion condition (e.g. occupational exposure risk) or a birth-country mismatch. Reuses
`PatientObservation.Code` matching for both the clinical-history-guideline check and the
exclusion-condition check — the same generic coded-fact pattern §5.1's indication matching
established, rather than inventing a parallel mechanism.

One more real-data format surprise, caught before parsing: `immunityBirthDate` uses `MM/DD/YYYY`
(e.g. `"01/01/1957"`), not the `yyyyMMdd` format used everywhere else in this dataset. Confirmed
against all 4 real instances before writing the parser, same discipline as every date/duration
field before it.

Tested against the spec's own worked example (Measles: guideline `"020"`, birth date
`1957-01-01`, exclusion `"055"` "Health care personnel") plus Varicella, which has a genuinely
country-restricted rule (`birthCountry: "U.S."`) — the one real fixture in this dataset that
exercises the country-mismatch branch, including an explicitly-flagged inference for what
happens when the patient's country of birth isn't on file at all (treated as a mismatch, not
assumed to match).

## §7.3 Determine Contraindications

A real data limitation surfaced immediately: Table 7-4 lists "Active Patient Observations" and
"Adverse Reactions" as two separate patient attributes, and Tables 7-5/7-6 treat "describes an
observation" and "describes an adverse reaction" as two distinct conditions — but the real XML
has only ONE `observationCode` field per contraindication entry, with no structural marker
distinguishing which kind a given code represents (checked the Schedule file's own 277-entry
`observations` catalog too — its `group` field is empty on every single one). Real codes clearly
span both concepts (`"007"`/"Pregnant" is an observation; `"091"`/"Severe allergic reaction after
previous dose of Measles" is an adverse reaction) with no way to tell them apart from the data
alone. Rather than guess at a distinction the data doesn't encode, `Patient` now has both
`ActiveObservations` and `AdverseReactions` as separate collections, and a contraindication's
code is checked against **either** — a documented simplification of the two-condition table, not
a guess at hidden structure. `MatchingAdverseReaction_NotActiveObservation_ContraindicationStillApplies`
proves the dual-bucket check actually works.

Table 7-6 (vaccine-level) has a real structural subtlety worth remembering: its age window lives
on the *matched contraindicated-vaccine entry*, not on the contraindication as a whole (confirmed
against the XSD — `vaccine > contraindication` has no `beginAge`/`endAge` of its own). That means
vaccine-type matching has to happen **before** the age check, the reverse of the antigen-level
table's ordering, where age dominates first. Getting this backwards would silently apply the
wrong vaccine's age window.

**A mistake caught before it shipped**: while building the vaccine-level test fixture, I'd
initially assumed the real `"089"`/`"155"`/`"186"` → MMRV (CVX 94) contraindication data lived in
the Varicella antigen file, since it's about a Varicella-related reaction. It's actually in the
**Measles** file — MMRV covers Measles/Mumps/Rubella/Varicella together, so a Varicella-reaction
contraindication naturally lives wherever the combination vaccine's antigen entries are defined,
not necessarily the antigen the reaction is "about." Caught by a routine `Single()`-uniqueness
verification against the real files before shipping (a habit from earlier rounds paying off
again) — the query against the Varicella file returned zero matches, which is what surfaced it.

Table 7-7's series-level combination (`IsContraindicatedPatientSeries`) is intentionally a pure
function over two caller-supplied booleans rather than something that derives "the preferable
vaccines for a relevant patient series" internally — that's a genuinely separate concept (which
target doses' preferable vaccines apply to a whole series) that doesn't exist as a built concern
in this codebase yet, same honest-scoping pattern as everywhere else in this project.

## §7.4 Determine Forecast Need

Table 7-10 is an 8-column decision table, but only Column 1 (the positive "should forecast"
case) requires all its conditions to hold together — the other seven columns are each an
independent negative gate that fires on one failing condition, with every other condition
marked "-" (don't care). Since more than one of those gates could theoretically be true at the
same time (a patient could be both aged out and contraindicated in the same evaluation) and the
table never states a priority among them, the evaluation order in `DetermineForecastNeed` is an
explicit inference, not spec-grounded: Contraindicated → Immune → Aged Out (two variants) →
seasonal → base dose-status logic, roughly permanent/severe reasons before temporary ones. Flag
this if you ever find spec text that states an explicit priority — it would be a straightforward
fix, just not one I could ground in what's been read so far.

The same forward-dependency shape from Interval/Vaccine Conflict shows up again:
`FORECASTDTCAN-1` ("candidate earliest date") is itself computed by §7.5 Generate Forecast
Dates, which doesn't exist yet. Rather than default it to something that could silently produce
a wrong "Aged Out" result for the common case of an unbounded max age (both defaulting to
12/31/2999 would make `candidateEarliest < maxAgeDate` false — i.e. "aged out" — for a totally
normal, unrestricted series), it's a nullable parameter: pass `null` until §7.5 exists, which
skips that specific gate rather than guessing at its value.

One new small reference-data piece needed grounding first: `<seasonalRecommendation>` (start/end
date per dose, used for flu-style seasonal windows). 53 real populated instances across the
dataset, confirmed `yyyyMMdd` format and at-most-one-per-dose before parsing. Real Influenza data
provides one direct fixture; the remaining seven rule columns are tested as pure decision logic,
since there isn't much more real data to ground in beyond the seasonal window itself and the
inputs are already well-established concepts from earlier work (target dose statuses, immunity,
contraindication).

## §7.5 Generate Forecast Dates (core date calculation)

The biggest single piece so far in Forecast: a "patient series forecast" is really six related
dates (`FORECASTDTCAN-1` candidate earliest date, then `FORECASTDT-1` through `FORECASTDT-6`),
computed via a mix of MAX() aggregation and tiered fallback rules. Scoped tightly to just the
date math this round — recommended-vaccine selection (`FORECASTRECVAC-1`), administrative
guidance text (`FORECASTGUIDANCE-1`), and forecast dose number (`FORECASTDN-1`) are each
separate, real pieces of work left for later, not folded in here.

Table 7-12 states something genuinely different from almost everywhere else in this codebase:
*"If an attribute value is empty, then the date calculations will remain empty. No assumptions
will be made for the attribute."* Chapter 6 leans on 1900/2999 sentinel defaults everywhere;
several of these Forecast inputs are deliberately **not** defaulted and must propagate as
genuinely absent through the calculation (e.g. `FORECASTDT-3`'s past-due date can be blank,
`FORECASTDT-4`'s latest date can be blank). Every parameter into `GenerateForecastDates` is
therefore nullable, and the two calculation functions treat null as "skip this component" rather
than substituting a value — tested explicitly (`PastDueDate_IsBlank_WhenNoLatestRecAgeOrIntervalDate`,
`LatestDate_IsBlank_WhenNoMaxAgeDate`).

One subtlety worth remembering: whether "no maximum age date" means *no AgeRule exists for this
target dose at all* versus *an AgeRule exists but its own `maxAge` sub-field is empty* changes
the right value to pass in — the latter already resolves to 2999-12-31 upstream (§6.4's own
established default), the former should stay genuinely null here. This function doesn't decide
that distinction itself; it trusts the caller, who already knows which case they're in.

Two of the six `FORECASTDTCAN-1` components — `latestConflictEndDate` and
`latestInadvertentAdministrationDate`/`mostRecentAdministeredDate` — remain caller-supplied
rather than internally derived this round; wiring them from the orchestrator's already-tracked
evaluation history is a reasonable next increment, not a fundamentally new problem.

`ForecastIntervalDates` reuses `EvaluatePreferableInterval`'s existing reference-point grouping
and temporal-selection logic to compute "latest of all X interval dates" rather than
reimplementing that machinery — proven against the same real HepB Dose 3 two-group interval data
used throughout this project's Interval work, including confirming that a reference-point group
with no `earliestRecInt`/`latestRecInt` at all is correctly excluded from the MAX rather than
treated as a zero or sentinel.

### FORECASTRECVAC-1: recommended vaccine selection

One field spotted early in this project but deliberately left unparsed until now:
`forecastVaccineType` on `preferableVaccine` — a real, meaningful binary flag (742 "N" / 347 "Y"
across all 1089 real entries, no other values), distinguishing preferable vaccines that are
merely valid for evaluation purposes from the smaller subset actually eligible to be forecast/
recommended going forward. `DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine`
implements the rule directly: flag must be "Y", no applicable vaccine contraindication, and the
candidate's own age window must contain either the forecast's earliest date or its adjusted
recommended date (either is sufficient, not both).

Worth remembering for the next real fixture lookup in this file: not every real seriesDose has
exactly one `preferableVaccine` entry — MenB-4C's Dose 3 has two. Tests here filter by CVX
explicitly rather than assuming `.Single()` is always safe, a small but real correction made
while building this round's fixtures rather than discovered later as a flaky-looking test.

### FORECASTDN-1: forecast dose number, FORECASTGUIDANCE-1: admin guidance text

Both closed out cleanly, alongside §7.6. `DetermineForecastDoseNumber` is genuinely simple once
the inputs are resolved — count of satisfied target doses that "count" (no seasonal
recommendation, or a seasonal one with a qualifying administered date) plus 1.

`GenerateForecastGuidance` needed three more previously-unparsed text fields grounded first:
`seriesAdminGuidance` (series-level regimen guidance, 250 real instances/211 non-empty),
`indication/guidance` (791/136), and `contraindicationGuidance` on both antigen- and
vaccine-level contraindications (392/16 combined — genuinely rare, but real). One precision
worth remembering: the rule's exact wording is "active patient observation," not "observation or
adverse reaction" — unlike `EvaluateContraindications`' applicability check, which deliberately
checks both buckets because the data can't tell them apart, this one is specific enough to
implement literally. `AdverseReactionAlone_DoesNotTriggerGuidance_UnlikeContraindicationApplicabilityCheck`
locks in that the two functions are allowed to disagree here, on purpose, because the spec text
itself differs between them.

### §7.6 Validate Recommendation

The smallest piece in this chapter, and a nice payoff for having built §7.1's context-aware
`EvaluateConditionalSkip` properly rather than as a one-off: §7.6 is the *same* Conditional Skip
machinery, called again with the forecast's own `EarliestDate` as the reference point instead of
the assessment date (the spec's own instruction: "In CONDSKIP-2, the Earliest Date is used").
`ValidateRecommendation.IsValid` is a thin wrapper — it introduces no new decision table of its
own — that answers "will this recommendation still make sense by the time its earliest date
actually arrives?" Tested against the same real Hib fixture used for §7.1's Evaluation-vs-Forecast
context tests, since it's the same underlying mechanism exercised from a different angle.

**Chapter 7 is now complete.** Every sub-step from §7.1 through §7.6 is implemented and tested
against real CDC data, closing out the entire Forecast chapter alongside Chapter 6's full
evaluation pipeline.

## §8.1/§8.2: starting Chapter 8

Chapter 8 introduces a genuinely new foundational concept — **Series Group** — from §5.1's
`<selectSeries>` element, which was never parsed when `CreateRelevantPatientSeries` was
originally built (nothing needed it then). Real HepB data alone has 18 series split across 2
series groups ("Standard" and "Increased Risk," cross-referencing each other via
`equivalentSeriesGroups`), with per-series `seriesPriority` ("A"/"B"/"C"), `seriesPreference`
(a tie-break rank), and `defaultSeries`/`productPath` flags — none of it previously modeled.

**A real bug caught by the sweep, not by a failed test run**: my first `SeriesGroupInfo` draft
made `seriesPreference` required, since a quick manual check of a few series suggested it was
always populated. The full 30-file sweep (a habit that's paid off repeatedly in this project)
found 12 real series — several "Shared Clinical Decision Making" series among them — where it's
genuinely absent. Fixed before writing a single line of evaluation logic against it, not after
a crash.

Scoped this round to §8.1 (Pre-Filter) and §8.2 (Identify One Prioritized Patient Series) — the
two pieces that don't need the full scoring machinery (§8.3-8.7), which introduces further new
concepts (`forecast finish date`, `product path`, `completable`) worth their own grounding pass
rather than rushing. §8.2 is a genuine shortcut: many series groups resolve to an obvious single
winner (one scorable series, or a clean complete/in-process/default pick) without ever needing
the point-scoring system in §8.4-8.6.

One inference worth flagging: SELECTSCORE-2's Bullet 2 ("earliest valid dose before the maximum
age to start date") doesn't say what happens when there's no `maxAgeToStart` at all — treated
as unbounded/always-satisfied here, consistent with how an absent age ceiling is handled
elsewhere in this codebase, but not literally spec-stated for this specific rule.

Real data made a genuinely good test fixture here: HepB's "Increased Risk" group has 8 Risk-type
series with an actual priority split (6 at "B", 2 at "A" — Dialysis and Recombivax), which
directly exercises Bullet 1's "highest priority in the group" logic without needing synthetic
data.

## §8.3/§8.4: classification and the first scoring table

§8.3's `DetermineScoringCategory` folded naturally into the same file as §8.2's SELECTB-6/16/21
helpers, since Table 8-5 is literally titled after this section and reuses those same
definitions directly. One structural fact worth remembering when reading Table 8-5: by the time
a series group reaches §8.3 at all, §8.2 has already ruled out a complete or in-process count of
exactly 1 — either would have won outright as the single prioritized series without needing to
score anything. That's what makes Table 8-5's three columns collectively cover the cases that
actually arrive here, even though the table itself doesn't state that reasoning explicitly.

One combination Table 8-5 doesn't name an outcome for, flagged rather than guessed at: zero
complete, zero (or one) in-process, but not every scorable series has zero valid doses either
(e.g. a series with 1+ satisfied dose whose forecast status is something other than
Complete/NotComplete, like Contraindicated). `DetermineScoringCategory` returns `Undetermined`
for this case rather than silently picking one of the three named categories.

§8.4 turned out to be the simplest point-scoring table in the chapter — a single condition
("has the most valid doses"), worth exactly +1 if this series uniquely has the max, 0 if tied,
-1 otherwise. `ScoreCompletePatientSeries.Execute` is a genuinely small, pure function as a
result. §8.5 (In-Process) and §8.6 (No Valid Doses) are considerably larger — 5 and 3 conditions
respectively, introducing `forecast finish date` (SELECTB-12, itself a calculation combining a
forecast's earliest date with the latest minimum interval across remaining target doses),
`completable` (SELECTB-3), `closest to completion` (SELECTB-5), and `product patient series`
(SELECTB-23, already trivial via `SeriesGroupInfo.IsProductPath`) — each worth its own grounding
pass rather than rushing through both in the same round §8.4 was built in.

## §8.5 In-Process Patient Series scoring

The biggest single scoring piece — five conditions instead of §8.4's one, and two of them
(`SELECTB-12`'s forecast finish date, `SELECTB-3`'s completable check) needed real calculation
rather than just a lookup.

**`SELECTB-12` (forecast finish date)** turns out to have a subtle implementation problem worth
knowing about: "the latest minimum interval from the remaining target dose(s)" can't be computed
by comparing `DurationExpression` values directly — "4 weeks" vs. "1 month" only has a
well-defined answer once anchored to a real date, since a month's length varies. Solved by
applying each remaining dose's `MinInt` duration to the *same* `earliestDate` anchor and taking
whichever resulting date is latest, which is mathematically equivalent without ever needing to
compare two durations in the abstract. One known simplification flagged in code: this doesn't
run `TemporalRuleSelector`'s version-selection over each dose's interval rules first, so a dose
with multiple temporally-versioned intervals (the COVID-19-style case elsewhere in this dataset)
could let a superseded rule's duration win the MAX if it's longer than the current one. Not
exercised by any real fixture in this project's tests yet, but a real gap if this function is
ever pointed at one.

**A genuine spec-text inconsistency, reconciled rather than inherited**: `SELECTB-5` ("closest
to completion") is worded as a strict "less than" comparison against every other series, which
under a literal reading can never be true for two tied series at once — yet Table 8-9 itself has
an explicit "true for two or more scorable patient series → 0" column for this exact condition.
`ScoreInProcessPatientSeries` doesn't inherit that gap: it separately detects a tie for the
group's minimum not-satisfied-dose-count and scores it 0, matching the table's own three-way
shape rather than the stricter literal wording of the underlying rule text. Locked in with a
test (`ClosestToCompletion_TiedMinimum_ScoresZeroForBoth_DespiteSelectB5sStrictWording`) named
specifically to make that reconciliation obvious to a future reader, not just correct by
accident. `SELECTB-11` ("can finish earliest") doesn't have this problem — its own wording
already uses "on or before," so ties there behave straightforwardly.

Every test in this round traces its expected point total by hand against the five conditions
before being written, given how easy it'd be for a sign error in one condition to hide behind
four others cancelling out correctly.

## §8.6 No Valid Doses scoring

Smaller than §8.5 (3 conditions instead of 5), but with two things worth knowing about:

**A deliberate sign inversion, confirmed against the literal table text rather than assumed
from §8.5's pattern**: §8.5 rewards staying on a product-specific path once you've already
committed doses to it (+2). §8.6 *penalizes* being product-tied when scoring series with zero
doses given yet (-1 if product, +1 if not). This makes sense once you notice the context split —
a product-specific path carries supply/availability risk that matters more when nothing has
started yet than when you're already partway through it — but it would be an easy "obviously a
copy-paste bug" fix for someone skimming the code without checking the spec. Flagged explicitly
in the doc comment so nobody "corrects" it.

**"Start date" (needed by `SELECTB-14`) is never actually defined anywhere in the spec** — it
appears in exactly one sentence and nowhere else, checked broadly. Since this scoring path only
applies to series with zero valid doses (nothing given yet), the most defensible reading is
`SeriesGroupInfo.MinAgeToStartDate` — reference data that already exists for exactly this
purpose, and whose nullability matches `SELECTB-14`'s own "with a start date" phrasing (implying
some series won't have one). Flagged as an inference, not a quoted definition.

Also hit the same tie-vs-strict-comparison issue as §8.5's `SELECTB-5`: `SELECTB-14` is worded
as a strict "before" that can't be true for two tied series, while Table 8-11 has an explicit
tied→0 column. Reconciled the same way — detect the tie separately rather than inherit the gap.

**Caught my own mistake before shipping**: while building a real-data test using HepB's series
group "2" (Dialysis and Recombivax both share `minAgeToStart` "20 years," genuinely earlier than
the other six series at "60 years"), I initially assumed Dialysis and Recombivax would score
identically since they tie on the start-date condition. A quick real-data check before finalizing
the test showed Recombivax is `productPath: "Yes"` while Dialysis is `"No"` — they don't score
the same at all, because of the very sign-inversion this round introduced. Fixed by comparing
Dialysis against a different, `productPath`-matched series instead, which cleanly isolates the
one condition the test was actually meant to demonstrate.

## §8.7 Select Prioritized Patient Series

The smallest step in the whole scoring pipeline. `SELECTBEST-1` ("the score is the sum of all
points awarded") turned out to need no code at all — `ScoreCompletePatientSeries`/
`ScoreInProcessPatientSeries`/`ScoreNoValidDosesPatientSeries` already return the fully-summed
total for whichever table applied, so there's nothing left to sum by the time a series reaches
this step. `SELECTBEST-2` (pick the highest score, tie-break by best `seriesPreference`) is the
only real logic here, and it's genuinely simple: max, then a secondary min on preference among
whoever's tied.

Tested against HepB's real, distinct 1-through-10 preference ordering in series group "1" —
including a case that deliberately lists the worse-preference series *first* in the input list,
to confirm the tie-break is a real comparison and not just "whichever came first." Also tests
what happens when a tie survives even the preference tie-break (two series artificially given
the same score and the same preference number, which can't happen within one real series group
but isn't something the function should assume away) — resolves to "no single winner" rather
than picking arbitrarily, consistent with every other "no resolution defined" case elsewhere in
this project.

## §8.8 Determine Best Patient Series — Chapter 8 complete

The genuine finale, and structurally different from everything else in the chapter: §8.1-8.7
all operate *within* one series group; §8.8 is the only step that reaches *across* groups, via
`equivalentSeriesGroups`. It runs once per series group's own prioritized series (§8.7's output),
after every group for an antigen has already picked one — which is why `DetermineBestPatientSeries`
is deliberately a pure function over pre-resolved cross-group facts (is *this* series complete,
does an *equivalent* group have a complete or Risk-type prioritized series) rather than something
that walks a patient's full set of groups itself. That walk — compute every group's prioritized
series first, then cross-reference each one's equivalent group — is real orchestration work of
its own, left for whenever this gets wired into an end-to-end flow, same pattern as
`EvaluateDoseAgainstTargetDose` existing well before `EvaluateSeriesHistory` tied multiple
evaluations together.

The chapter's own framing is worth remembering when using this: "one or more non-redundant best
patient series will remain" — a Standard-group series and a Risk-group series can both end up
"best" simultaneously for the same antigen, because Table 8-14's Column 2 explicitly lets an
incomplete Risk series stand as best when nothing better covers it, rather than requiring every
best series to be complete. Tested against real HepB `SeriesType` data (group "1" is entirely
Standard, group "2" is entirely Risk, and HepA's real Evaluation Only fixture from §8.1 covers
the "never best" case for that series type), plus a case confirming that completion alone wins
regardless of how contradictory the other three inputs are — Column 1 has no dependency on them
at all, and the test deliberately feeds in values that would fail every other column to prove it.

**Chapter 8 is now complete.** All eight sub-steps, §8.1 through §8.8, are implemented and
tested against real CDC data — Pre-Filter, Identify One Prioritized, Classify Scorable, all
three point-scoring tables (Complete/In-Process/No Valid Doses), Select Prioritized, and
Determine Best. Alongside the completed Chapters 6 and 7, that's evaluation, forecasting, and
now series selection all built and proven.

## §9.1/§9.2: starting the final chapter

**A real, load-bearing data-source correction, caught before writing a single line of
classification logic**: the Schedule file's `vaccineGroupToAntigenMap` table looks like the
obvious source for "which antigens does this vaccine group cover" — but checking it against
real data first showed it's incomplete for genuine multi-antigen groups. It lists `"MMR" ->
"Measles"` and `"DTaP/Tdap/Td" -> "Diphtheria"` — one antigen each, dropping Mumps/Rubella and
Tetanus/Pertussis entirely, even though the spec's own narrative text explicitly calls both out
as multi-antigen groups. The complete, verified-consistent source turned out to be each antigen
file's *own* `<series><vaccineGroup>` field — already parsed into `AntigenSeries.VaccineGroup`
since early in this project, for an entirely different reason. Grouping all 30 antigen files by
that field recovers the correct membership (`MMR` = Measles+Mumps+Rubella, `DTaP/Tdap/Td` =
Diphtheria+Tetanus+Pertussis) and confirms every other real vaccine group is genuinely
single-antigen. `VaccineGroupClassification.Classify` is deliberately a pure function over a
pre-derived antigen list rather than something that reads the Schedule table itself, so it can't
silently regress to the incomplete source.

The Schedule file's `<vaccineGroups>` element *is* the right source for one thing:
`administerFullVaccineGroup` (needed by `FORECASTDN-2`). Real data: only 2 of 26 groups specify
it at all — MMR is `"Yes"`, DTaP/Tdap/Td is `"No"` — every single-antigen group leaves it unset,
which makes sense once you notice `FORECASTDN-2`'s MIN/MAX choice is only meaningful when a
group's forecast could be built from more than one contained forecast.

§9.1's date-aggregation rules (`FORECASTVG-2` through `6`) deliberately don't compute the vaccine
group's own `EarliestDate` themselves — that's genuinely §9.2's or §9.3's job (single-antigen is
a trivial pass-through; multi-antigen needs the "priority patient series forecast" concept,
deferred to next round), so it's taken as an already-resolved parameter here rather than
guessed at or duplicated.

§9.2 (`SINGLEANTVG-1/2`) is about as small as a sub-step gets in this whole project — a status
pass-through and a MIN over dates — which is exactly right for the "trivial" case a single
antigen vaccine group represents.

## §9.3 Multiple Antigen Vaccine Group — core Chapter 9 logic complete

**Another real terminology mismatch, caught by grounding before coding**: the spec's own text
describes `FORECASTPRIORITY-1`'s condition as an "interval priority flag" set to `'Y'`. Swept
all 490 real preferable-interval rules across every file before writing anything against it —
the literal string `"Y"` never appears even once. The only non-empty value in the entire dataset
is `"override"` (30 real instances, concentrated in Pertussis — fitting, since Pertussis belongs
to the real `DTaP/Tdap/Td` multi-antigen group this section exists for). `IsPriorityOverride`
treats `"override"` as the real-world equivalent of the spec's described `'Y'` state, since
nothing else in the data could plausibly mean anything different — flagged as an inference
grounded in exhaustive real-data coverage, not a quoted definition.

Table 9-4's status cascade turned out clean once translated out of decision-table form: it's a
strict priority order (`Contraindicated` → `AgedOut` → `NotRecommended` → `NotComplete` → all
`Immune` → `Complete`), each condition checked only after every earlier one has failed. Worth
noting for a future reader: the final `Complete` fallback needs no explicit "are they all
Complete or Immune?" check of its own — by the time the cascade reaches it, every other status
has already been ruled out and "not all Immune" has been confirmed, so nothing but Complete/
Immune could remain among the contained forecasts.

**Core Chapter 9 logic is now complete**: classification (§9.1), date/dose-number aggregation
(§9.1), the single-antigen trivial case (§9.2), and the multiple-antigen status cascade plus
priority-forecast/earliest-date rules (§9.3) are all implemented and tested against real CDC
data.

## §9.1 finished: FORECASTVG-1/8/9 — Chapter 9 rule logic complete

The three pieces held back from the earlier §9.1 round turned out to be genuinely small, as
expected: `FORECASTVG-1` (containment) is a plain 3-condition AND; `FORECASTVG-8` (recommended
antigen) is a 2-condition AND; `FORECASTVG-9` (recommended vaccine aggregation) is a filter,
flatten, and dedupe over already-built §7.5 output (`DetermineRecommendedVaccine`'s CVX codes).
`FORECASTVG-7` (forecast reasons) needed no function at all, for the same reason `SELECTBEST-1`
didn't back in §8.7 — both are literally "collect this field from every contained forecast,"
not a decision.

**Every individual business rule across §6, §7, §8, and §9 is now implemented and tested against
real CDC data.** What remains before this is a running end-to-end pipeline is the deferred
orchestration work flagged throughout §8 and §9: computing every relevant series' forecast,
selecting best patient series per series group, and merging them into vaccine group forecasts —
wiring together dozens of already-proven pure functions into one patient-level walk, the same
shape of work `EvaluateSeriesHistory`/`EvaluatePatientSeriesHistory` did for Chapter 6.

## The §7 per-series forecast orchestrator

The first real piece of the deferred orchestration work: `GeneratePatientSeriesForecast` wires
together §7.1 (Conditional Skip, Forecast context), §7.2 (Evidence of Immunity), §7.3
(Contraindications), §7.4 (Determine Forecast Need), §7.5 (all of Generate Forecast Dates -
core dates, recommended vaccine, dose number, guidance), and §7.6 (Validate Recommendation) on
top of one series' Chapter 6 evaluation output (`SeriesHistoryResult`). §8's cross-series-group
selection and §9's vaccine group merge remain separate, larger pieces on top of this one.

**A real bug caught and fixed before it shipped, not discovered later as a flaky test**: my
first draft stubbed the interval reference-date resolver as `_ => null` in the six-date
calculation, while the candidate-earliest-date calculation used the real resolver — meaning
`latestEarliestRecIntervalDate`/`latestLatestRecIntervalDate` would have silently always
returned null, even when real interval data existed to resolve them. Fixed by extracting one
shared `BuildIntervalReferenceResolver` used consistently by both calculations, rather than two
copies that could quietly drift apart. Every function signature this orchestrator calls into
was also individually re-verified against its actual source before wiring it in, rather than
trusted from memory of having written it several rounds ago.

Two inputs remain caller-supplied, matching gaps flagged since §7.5 was first built:
`latestConflictEndDate` and `latestInadvertentAdministrationDate` need forward-looking
calculations (a "will this future dose conflict with what's already given" check, and
inadvertent-administration tracking) that don't exist yet - both are optional parameters,
defaulting to null, which the candidate earliest date calculation already treats as "skip this
component," not a sentinel.

**A genuinely useful real-data finding surfaced while building the capstone test, not
invented**: every real `preferableVaccine` entry for HepB Dose 3 has `forecastVaccineType`
`"N"` - none are forecast-eligible. Checked this *before* writing the test's assertion (having
just been burned by a similar near-miss with `DetermineRecommendedVaccine` a few rounds back),
so the capstone test correctly expects an *empty* recommended-vaccines list for that fixture,
with a second, separate test reusing the real MenB-4C fixture (already proven `Y`-flagged) to
demonstrate the pipeline actually surfaces a recommended vaccine when one exists.

## The §8 per-series-group orchestrator

The natural next layer: `SelectPrioritizedPatientSeriesForGroup` runs the *entire* §8.1-§8.7
pipeline (Pre-Filter → Identify One Prioritized shortcut → Classify Scorable → whichever of
§8.4/8.5/8.6 applies → Select Prioritized) for one series group, using this project's own
Chapter 6/7 orchestrators as its input. §8.8's cross-group logic (which needs every group's
prioritized series for an antigen, cross-referenced via `equivalentSeriesGroups`) remains a
further layer on top of this one, not included here.

**A real bug caught by hand-tracing the code before testing, not by a failing assertion**: my
first draft computed a series' earliest-valid-dose-date with `.DefaultIfEmpty().Cast<DateOnly?>()`
over a non-nullable `IEnumerable<DateOnly>`. `DefaultIfEmpty()` on a non-nullable sequence
inserts `default(DateOnly)` — `0001-01-01` — not null, so a series with zero satisfied doses
would have silently returned a bogus non-null date instead of the `null` the return type's own
contract implied. It happened to be harmless at the one call site that existed at the time
(guarded by a `ValidDoseCount > 0 &&` check that never lets the bogus value get read), but
relying on incidental protection at a single call site isn't a fix, just a reason it hadn't bitten
yet. Rewrote it as a plain length-check instead.

**A second real bug, caught while hand-verifying the test fixtures rather than assuming they'd
work**: the first draft of the test suite used a fixed absolute date (`2020-01-01`) for every
synthetic "valid dose," independent of whatever DOB each test used. Checking the real
`maxAgeToStart` values for the two HepB series involved (both genuinely 19 years) showed that
date landed *past* the threshold relative to the test's own DOB — which would have silently
routed several tests through the wrong code path (the "no scorable series, fall back to default"
branch) while still passing, for the wrong reason, because the default series test happened to
share a name with what the test was trying to prove. Fixed by making dose dates DOB-relative
instead of a fixed calendar date, then re-traced all five tests by hand against the actual
`PreFilterPatientSeries`/`IdentifyOnePrioritizedPatientSeries` logic before trusting them.

## §8.8: Chapter 8's orchestration complete

**A real compile error shipped and caught by the person running `dotnet test`, not by me** —
worth being upfront about rather than glossing over. `SelectPrioritizedPatientSeriesForGroup`
used `.Count` (property syntax) on `T[]` array-typed locals in two places; arrays don't expose a
`Count` property directly (only `.Length`), so the compiler resolved the bare `.Count` to the
`Enumerable.Count` extension method *group* instead, which then failed to compile against `==`
and as a method argument. Fixed both occurrences to `.Length`, then swept the rest of that file
and `GeneratePatientSeriesForecast.cs` by hand-checking every remaining `.Count` usage against
its actual declared type (all turned out to be genuine `IReadOnlyList<T>` properties, which do
support `.Count` correctly), and ran a repo-wide pattern search for the same mistake shape
before calling it fixed. This round's new file was checked against the identical pattern before
being shipped.

`DetermineBestPatientSeriesForAntigen` is the piece that finally closes out Chapter 8's
orchestration: it groups a patient's relevant series for one antigen by `SeriesGroupInfo.SeriesGroup`,
runs `SelectPrioritizedPatientSeriesForGroup` once per group, then cross-references each group's
own prioritized series against its `equivalentSeriesGroups` counterpart via
`DetermineBestPatientSeries` to decide the antigen's final "best patient series" set — which, per
the chapter's own framing, can genuinely contain more than one series or none at all.

Tested against HepB's real, complete bidirectional equivalence pair — "HepB 3-dose series"
(group 1, Standard, `equivalent="2"`) and "HepB risk 3-dose series" (group 2, Risk,
`equivalent="1"`) — including a case that traces exactly why a complete Standard series makes
its equivalent incomplete Risk series correctly drop out of the "best" set (already covered,
not needed), and a single-group case proving the orchestrator doesn't crash or misbehave when a
series' `equivalentSeriesGroups` points at a group that simply isn't part of the current input.
Every test's expected outcome was hand-traced against the real `EquivalentSeriesGroup` values
and Table 8-14's four columns before being written, not inferred from what "seemed right."

**With this, all of Chapter 8's orchestration is built**: `SelectPrioritizedPatientSeriesForGroup`
(§8.1-§8.7, per series group) and `DetermineBestPatientSeriesForAntigen` (§8.8, per antigen) sit
on top of `GeneratePatientSeriesForecast` (§7, per series), which sits on top of
`EvaluateSeriesHistory`/`EvaluatePatientSeriesHistory` (§4.4/§6). Only a §9 vaccine-group-merge
orchestrator remains before every chapter has both its rules AND its wiring complete.

## The pipeline is complete: `GeneratePatientForecast`

**This is the moment the whole project has been building toward.** `GeneratePatientForecast`
takes a patient and their raw administered dose history and produces merged vaccine group
forecasts — genuinely wiring together every layer built across this entire project: §4.2/§5.1
(organize history, find relevant series) → §4.4/§6 (evaluate immunization history, via
`EvaluatePatientSeriesHistory`) → §7 (forecast each series, via `GeneratePatientSeriesForecast`)
→ §8 (select best patient series, via `SelectPrioritizedPatientSeriesForGroup` and
`DetermineBestPatientSeriesForAntigen`) → §9 (merge into vaccine group forecasts, via the new
`MergeVaccineGroupForecast`). Raw doses in, a real forecast out — no mocks, no stand-ins,
anywhere in that chain.

**A real design gap discovered only by building the top-level orchestrator, not visible from
any single layer in isolation**: a single ANTIGEN vaccine group (like HepB) can legitimately
have more than one "best patient series" simultaneously — e.g. HepB's Standard and Risk series
groups both independently resolving to Complete via §8.8's Column 1 at the same time. That's not
a data inconsistency, it's redundant agreement. But `SingleAntigenVaccineGroup.Status` had been
built several rounds ago with a strict `.Single()`, which would have thrown on this entirely
real, reachable scenario. Fixed to tolerate multiple contained statuses that agree, while still
throwing loudly on a genuine disagreement (e.g. one Complete, one NotComplete) — a real
inconsistency SINGLEANTVG-1's own singular phrasing doesn't anticipate. This is exactly the kind
of gap that only surfaces once pieces actually run together, not from testing any one piece in
isolation — the reason this integration step mattered on its own, not just as glue code.

**An honest scoping decision in the end-to-end tests, made because of a real constraint**: this
sandbox has no `dotnet` runtime, so nothing here can be executed and empirically verified the
way `dotnet test` on your machine can. Real HepB has 10 Standard-type series competing in one
series group; for a patient with no active observations, all 10 become simultaneously relevant
and would genuinely compete in §8's scoring. Rather than assert an exact `EarliestDate` or dose
number for a 10-way competition I have no way to actually run and check, the end-to-end tests
deliberately scope the antigen catalog down to the single series already hand-verified in
isolation last round. The pipeline still runs every real stage genuinely end-to-end — this is a
scoping choice about what can be safely asserted here, not a limitation of the pipeline itself.
Running the true, full 18-series HepB catalog through this pipeline and confirming which series
actually wins is real, valuable follow-up work once you run it against a real `dotnet` runtime.

**With this, every chapter this project set out to build — §6 Evaluation, §7 Forecast, §8 Select
Best Patient Series, §9 Vaccine Group Merge — has both its business rules AND its end-to-end
orchestration built and tested against real CDC data.** What's left is genuinely a different
phase: the handful of documented, deliberately-deferred gaps (forward-looking conflict/
inadvertent-administration dates, multi-antigen priority-forecast wiring, running the full
antigen catalog through the pipeline for real), and then `Cdsi.Api` — turning this into an
actual running service.

## `Cdsi.Api` — the dockerized web API

The pipeline is now reachable over HTTP, not just from C# callers. `Cdsi.Api` is a minimal-API
ASP.NET Core 8 project wrapping `GeneratePatientForecast` behind two endpoints, containerized
with a real multi-stage Dockerfile and a `docker-compose.yml` for local builds and runs.

### Running it

```bash
docker compose up --build
```

This builds the image from the root `Dockerfile` and starts the API on `http://localhost:8080`.

Without Docker, `dotnet run --project src/Cdsi.Api` works too - the same `FindDataDirectory`
pattern already proven in `Cdsi.Demo` walks up from the executable's location to find `Cdsi.sln`
and resolves `data/` from there, so no environment variable is needed for local development.

### Swagger, gated to non-Production

`GET /swagger` (interactive UI) and `GET /swagger/v1/swagger.json` (raw spec) are only exposed
when `ASPNETCORE_ENVIRONMENT` isn't `Production` - `app.Environment.IsDevelopment()` gates the
`UseSwagger()`/`UseSwaggerUI()` middleware. This is a clinical data API; leaving interactive docs
always reachable would mean anyone who can hit the port can browse the full API surface and fire
test requests at it. `docker-compose.yml` explicitly sets `ASPNETCORE_ENVIRONMENT=Production`,
so the containerized API correctly never exposes Swagger.

For local `dotnet run` to default to `Development` (and therefore have Swagger available) without
needing an environment variable set by hand, `Properties/launchSettings.json` was added - a file
every scaffolded ASP.NET Core project gets from `dotnet new webapi` that this project didn't have,
since it was hand-built rather than scaffolded. Without it, local `dotnet run` would have silently
defaulted to `Production` too (ASP.NET Core's own actual default when no environment is set at
all), disabling Swagger locally as well - caught and fixed as part of the same change, not left
as a follow-up gap.

### Endpoints

- `GET /health` - liveness/readiness check, also reports how much reference data loaded
  (antigen/series/vaccine-group counts) - useful for confirming the data volume mount actually
  worked, not just that the process is up.
- `POST /api/v1/forecast` - the real thing. Request body maps directly to `Patient`/
  `VaccineDoseAdministered` (see `Contracts/ForecastRequestDto.cs`); response is one entry per
  vaccine group forecast (see `Contracts/ForecastResponseDto.cs`), with enums represented as
  their string names (`"NotComplete"`, `"SingleAntigen"`, etc.) rather than numbers, for a JSON
  API a real EHR integration will actually read by hand while debugging.

### The data volume, not baked into the image

Consistent with this project's stated top priority ("easy updates when CDC schedule/logic
changes"): `data/` is mounted read-only into the container (`./data:/data:ro` in
`docker-compose.yml`) rather than `COPY`'d into the image. Updating the CDC's supporting data is
a matter of replacing files under `./data` and restarting the container - not rebuilding the
image. `ReferenceDataRepository` is loaded once at startup as a singleton and resolved eagerly
(not lazily on first request), so a bad data path fails fast with a clear startup error instead
of surfacing as a confusing 500 on an EHR integration's first real request.

### Package versions, chosen deliberately rather than left to "latest"

This sandbox still can't reach nuget.org (not in the allowed-domains list) or execute `dotnet
build`, so every new package reference here was verified against real, current NuGet listings via
web search *before* being written into a `.csproj`, not guessed:

- **`Swashbuckle.AspNetCore` 6.6.2** - specifically confirmed (via a dedicated blog post found
  during that search) as the release that added native .NET 8 support, rather than picking
  whatever the newest major version happened to be (10.2.3 at the time of writing, which
  introduces its own breaking changes around `Microsoft.OpenApi` 2.x). A deliberately
  conservative, confirmed-compatible choice over the newest one.
- **`Microsoft.AspNetCore.Mvc.Testing` 8.0.11** - matches the runtime major.minor (`net8.0`)
  exactly, the standard alignment convention for first-party ASP.NET Core packages, confirmed to
  exist as a real published version rather than assumed.

### Tests

`Cdsi.Api.Tests` uses `WebApplicationFactory<Program>` - real HTTP calls against the real
`Program.cs` startup running in-memory, including real data loading from the real repo `data/`
directory. Not mocked at any layer. Covers the health check's real loaded-data counts, two real
HepB scenarios reused deliberately from elsewhere in this project (with an honest correction
made along the way - see below), invalid input (bad gender string, missing required field), and
the assessment-date default-to-today behavior.

**A mistake caught and fixed before shipping, not after**: an early draft of the two-dose HepB
test reused `GeneratePatientSeriesForecastTests`' own "`ForecastDoseNumber` should be 3" fixture
directly. But that fixture was built against a *deliberately scoped-down* single-series test
double - this request goes through the real, full, unscoped 18-series catalog, the same one
`HepBFullCatalogCompetitionTests` already established has three genuine competing candidates for
this exact dose history, two of which resolve to dose 3 and one to dose 2, without a confidently
knowable winner among them. Asserting exactly `3` here would have been an unverified guess
dressed up as a checked fact. Fixed to assert what's actually known: an in-process forecast
exists, for dose 2 or 3 - real coverage of the HTTP/JSON round-trip without overclaiming
precision this sandbox can't verify.

**A real compile error, caught on the first actual `dotnet test` run and fixed immediately**:
`.WithOpenApi()` was called on both minimal API endpoints, but that extension method belongs to
the separate `Microsoft.AspNetCore.OpenApi` package (Microsoft's own OpenAPI metadata generator,
`.NET 9`'s default), not Swashbuckle - a genuine conflation of two different OpenAPI mechanisms
that this sandbox's inability to run `dotnet build` couldn't catch in advance. `AddSwaggerGen()`
+ `AddEndpointsApiExplorer()` (the combination actually used here) discovers and documents
minimal API endpoints on its own; `.WithOpenApi()` isn't needed at all with that combination.
Fixed by removing both calls - `.WithName()` stays, since that's a core minimal-API method
unrelated to either OpenAPI package.

**A real runtime bug, caught by the first real integration test run against the actual HTTP
pipeline** - the kind this project's static analysis (brace checks, hand-tracing against real
data, source-code re-reads) genuinely cannot catch, since it only manifests when the framework's
own request pipeline runs. `Forecast_MissingRequiredDateOfBirth_Returns400` sent a request body
missing the required `dateOfBirth` field expecting a 400 - and got a 500 instead. The real cause,
visible directly in the test run's own logged exception: ASP.NET Core's minimal-API JSON binding
throws `BadHttpRequestException` for exactly this case (a missing required property), and that
exception type carries the correct status code (400) as its own `StatusCode` property - but the
custom `UseExceptionHandler` handler only special-cased `InvalidRequestException`, silently
discarding the framework's own correct status and falling through to the generic 500 branch for
everything else. Fixed by adding a branch that respects `BadHttpRequestException.StatusCode`
directly, and prefers the inner `JsonException`'s more specific message ("was missing required
properties, including the following: dateOfBirth") over the outer exception's more generic one
when available - both details visible directly in the failing test's own captured log output,
not guessed at.

## Next steps

**The end-to-end pipeline is complete and has been run successfully against the real, full
30-antigen catalog** (see "Run the whole pipeline yourself" above) — every chapter this project
set out to build has both its business rules and its orchestration implemented and tested
against real CDC data, wired together into one call (`GeneratePatientForecast`): raw
administered doses in, merged vaccine group forecasts out.

**All four originally-documented gaps are now closed** — §6.2's Completed Series,
`latestConflictEndDate`/`latestInadvertentAdministrationDate`, multi-antigen priority forecast
(§9.3's `MULTIANTVG-1`), and Recurring Dose (§4.4). See "Filling the gaps" above for each one's
own story. Every deliberately-deferred piece of this project's core CDSi logic has been
resolved, grounded in real data before implementation.

**`Cdsi.Api` is confirmed working end-to-end, including the real Docker build.** `dotnet test`
passes 327/327 (321 core + 6 API integration tests, real HTTP calls, real data loading, no
mocks), and `docker compose up --build` has been run for real: the multi-stage build completes,
the container starts, and the real data volume mount loads correctly (143 series across 30
antigens, 26 vaccine groups - the exact same counts every other real run of this project has
shown). One informational-only warning appeared and needs no action: the base image's own
default `ASPNETCORE_HTTP_PORTS` gets overridden by this project's explicit `ASPNETCORE_URLS`
setting, which is the intended behavior working as designed, not a problem.

What remains, roughly in order of what's most valuable next:

1. **Azure Functions as a second API surface, wrapping the same `GeneratePatientForecast` call**
   - explicitly requested as the next phase after the dockerized API. Likely an isolated-worker
     Azure Functions project (`Cdsi.Functions`) sharing `Cdsi.Core` and possibly `Cdsi.Api`'s own
     `Contracts` DTOs/mapping layer, rather than duplicating the request/response shapes.

At this point, every piece of this project - the core engine (all four chapters, all four
originally-documented gaps), the full 18-series HepB competition, MPL 2.0 licensing, and the
dockerized API - has been confirmed against a real `dotnet test`/`docker compose up` run, not
just reasoned about from this sandbox. Azure Functions is the one remaining piece still ahead of
that verification.
