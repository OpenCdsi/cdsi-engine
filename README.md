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
| Evaluate Immunization History (§4.4 orchestrator) | §4.4 | ✅ Implemented + tested — the two-pointer target-dose/administered-dose walk, wiring all 10 Ch.6 components together with real (not caller-supplied) Interval and Vaccine Conflict resolution. Now runs across every relevant series for a patient (`EvaluatePatientSeriesHistory`), with real cross-antigen Vaccine Conflict resolution proven end-to-end. See "The orchestrator" below for what's still deferred (Recurring Dose, Completed Series) |
| Forecast | §7 | ✅ Complete — all of §7.1-§7.6 implemented + tested (§7.1 Conditional Skip Forecast context, §7.2 Evidence of Immunity, §7.3 Contraindications, §7.4 Forecast Need, §7.5 Generate Forecast Dates incl. recommended vaccine/admin guidance/dose number, §7.6 Validate Recommendation) |
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
- **"Completed Series" (§6.2's Table 6-7) is still deferred**, and now for a more specific,
  grounded reason: its `seriesGroups` value in real data (always `"1"` in the current dataset)
  turns out to reference §5.1's `selectSeries`/`seriesGroup` concept — Chapter 8 "Select Best
  Patient Series" territory, which doesn't exist in this codebase at all yet. Resolving this
  properly needs that built first, not just more orchestrator plumbing.

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
  Evaluation/        Chapter 6 logical components — all 10 implemented — plus all of Chapter 7
                     Forecast: EvaluateEvidenceOfImmunity (§7.2), EvaluateContraindications
                     (§7.3), DetermineForecastNeed (§7.4), GenerateForecastDates +
                     ForecastIntervalDates (§7.5, core date calc), DetermineRecommendedVaccine
                     (§7.5, FORECASTRECVAC-1), DetermineForecastDoseNumber (§7.5,
                     FORECASTDN-1), GenerateForecastGuidance (§7.5, FORECASTGUIDANCE-1),
                     ValidateRecommendation (§7.6).
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
                     EvaluatePatientSeriesHistory (§4.4 per-patient orchestrator)
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

## Next steps

1. All 10 Chapter 6 logical components — ✅ done.
2. §4.4 per-series and per-patient orchestrators — ✅ done, real Interval and cross-antigen
   Vaccine Conflict resolution proven end-to-end. Recurring Dose and Completed Series remain
   documented gaps (see above).
3. **Chapter 7 Forecast (§7.1-§7.6) — ✅ complete.** All six sub-steps implemented and tested
   against real CDC data: Conditional Skip (Forecast context), Evidence of Immunity,
   Contraindications, Forecast Need, Generate Forecast Dates (core 6-date calculation,
   recommended vaccine, admin guidance, forecast dose number), and Validate Recommendation.
4. Still open within §7.5's date calculation specifically: wiring `latestConflictEndDate`/
   `latestInadvertentAdministrationDate`/`mostRecentAdministeredDate` from the orchestrator's
   tracked history instead of taking them as caller-supplied parameters — a real but small
   follow-up, not a new problem.
5. §8 Select Best Patient Series — genuinely comes AFTER §7 (see "Correction" above, now fully
   applicable since §7 is done). Needed to resolve §6.2's Completed Series condition and to
   pick a "best" series per series group once forecasts exist for all of them. Natural next
   chapter to start.
6. §9 Vaccine Group Merge.
7. `Cdsi.Api` (ASP.NET) + real Dockerfile target once the pipeline is complete.
