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
| Evaluate Vaccine Dose Administered | §6.1–6.10 | ✅ All 10 logical components implemented + tested |
| Evaluate Immunization History (§4.4 orchestrator) | §4.4 | ✅ Implemented + tested — the two-pointer target-dose/administered-dose walk, wiring all 10 Ch.6 components together with real (not caller-supplied) Interval and Vaccine Conflict resolution. See "The orchestrator" below for what's still deferred (Recurring Dose, Completed Series) |
| Forecast | §7 | ⏳ Not started |
| Select Best Patient Series | §8 | ⏳ Not started |
| Vaccine Group Merge | §9 | ⏳ Not started |

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

**Two things NOT implemented, both explicitly flagged in code comments, not silently skipped:**
- **Recurring Dose** (§4.4 step 5) — the spec gives it barely more than a one-line flag
  definition, no dedicated decision table like every other component got. All target doses are
  treated as non-recurring. This is a real, known gap: any series with a genuinely recurring
  target dose (Td boosters, annual flu/COVID, some risk series) will evaluate incorrectly past
  that point, not just theoretically.
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

"Completed Series" (Table 6-7) is the one §6.2 condition type still caller-supplied — it needs
another relevant patient series' completion status, which doesn't exist anywhere in this
codebase (no series-level status tracking has been built yet, only individual dose evaluation).
Everything else in §6.2 — Age, Interval, and Vaccine Count (which covers all the "by Age"/"By
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
  Evaluation/        Chapter 6 logical components — all 10 now implemented.
                     DoseEvaluationOutcome (shared result type for §6.4-6.9),
                     EvaluateDoseAdministeredCondition (§6.1), EvaluateConditionalSkip (§6.2),
                     EvaluateInadvertentVaccine (§6.3), EvaluateAge (§6.4),
                     EvaluatePreferableInterval (§6.5), EvaluateAllowableInterval (§6.6),
                     EvaluateVaccineConflict (§6.7), EvaluatePreferableVaccine (§6.8),
                     EvaluateAllowableVaccine (§6.9), SatisfyTargetDose (§6.10 aggregator).
  Pipeline/          OrganizeImmunizationHistory (§4.2), CreateRelevantPatientSeries (§5.1),
                     EvaluateSeriesHistory (§4.4 orchestrator)
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

1. All 10 Chapter 6 logical components — ✅ done.
2. §4.4 orchestrator (`EvaluateSeriesHistory`) — ✅ done, wiring real Interval and Vaccine
   Conflict resolution end-to-end. Two known gaps remain, both explicitly flagged in code:
   Recurring Dose isn't implemented (all target doses treated as non-recurring — a real gap for
   Td/flu/COVID-style series), and §6.2's Completed Series condition is still caller-supplied
   (needs cross-series status tracking, which doesn't exist yet — see below).
3. Series-level status tracking (is a relevant patient series 'Complete'?) across ALL of a
   patient's relevant series for one antigen group — this is what §6.2's Completed Series
   condition needs, and doesn't fit inside `EvaluateSeriesHistory` itself since it requires
   comparing across series, not within one. Natural next piece.
4. Wire `EvaluateSeriesHistory` up to run over every relevant series returned by
   `CreateRelevantPatientSeries` for a patient (currently it runs one series at a time, by
   design — but nothing yet drives it across a patient's full relevant-series set).
5. §7 Forecast — the natural next chapter now that evaluation is complete.
6. §8–9 Best Series / Vaccine Group selection.
7. `Cdsi.Api` (ASP.NET) + real Dockerfile target once the pipeline is complete.
