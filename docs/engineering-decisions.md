# Engineering decisions

Short decision records for the choices that shaped this codebase — including the
alternatives that were rejected and what would make me revisit each one. Reading this
should tell you more about how I work than the code diff does.

---

## 1. Compose a candidate space, then score it — instead of building "the best" itinerary

**Context.** A door-to-door journey has many defensible answers. Which airport you leave
from, which hub you connect through, whether you take the train or the cheap bus + trek,
whether you sleep in a bed or in a terminal — each combination is a different trip.

**Decision.** The composer enumerates the cross-product of those axes (bounded and
deduped) and hands the whole set to the scorer. Profiles are chosen *from real
alternatives*.

**Rejected: greedy single-path construction.** Cheaper to write and much faster, but it
makes "Fastest / Cheapest / Balanced" a lie — you end up re-labelling one result three
times, or bolting on special cases per profile. The moment a product needs to explain
*why* an option was chosen, a greedy builder has nothing to say.

**Cost accepted.** More provider calls and a combinatorial surface that must be bounded
(`MaxCandidates`, ≤4 flight legs, BFS pruning when a hop moves away from the target).

**Revisit if** the world data grows enough that the cross-product stops being tractable;
the fix is beam search over partial itineraries, not a return to greedy.

---

## 2. Normalize at the adapter boundary, never inside the engine

**Context.** Every travel provider has its own dialect: cents-as-int, local clock
strings, `low_cost` flags, per-leg currencies.

**Decision.** Each adapter owns a **private** wire record and maps it to canonical
`TransportOffer` / `StayOffer` / `ActivityOffer`. Application and Domain never see a
provider shape. Mock adapters in this repo carry fake wire records on purpose, so the
boundary is real and exercised, not theoretical.

**Rejected: passing provider DTOs inward and mapping "later".** It always leaks — a
`low_cost` boolean becomes a scoring input, then a UI conditional, and swapping the
provider means touching every layer. Provider semantics also differ subtly; converting
early forces you to answer "what does this actually mean in our model?" once, in one
place.

**Payoff.** Swapping a mock for a live integration is a single DI line
(`AddSingleton<IFlightSearchProvider, ...>`). That claim is testable and true here.

---

## 3. Min-max normalize per candidate set before weighting

**Context.** Raw metrics are incommensurable: price in the hundreds, duration in the
thousands of minutes, risk in [0,1]. Weighting raw values means the biggest unit wins.

**Decision.** Normalize each metric across the candidates being compared (0 = best in
set, 1 = worst), then apply per-profile weights that sum to 1. A metric with no spread
scores 0 for everyone — it cannot differentiate, so it must not punish.

**Rejected: fixed global normalizers** (e.g. "price / 5000"). They need constant
re-tuning as the market moves and produce meaningless scores on unusual routes.

**Consequence accepted.** Scores are *relative to the result set*, so they are not
comparable across searches. That is the honest reading of "which of these is better for
me", and the UI labels the total as such.

---

## 4. Return a breakdown and a sentence, not a number

**Decision.** Every scored itinerary carries its component table (normalized × weight =
weighted) and a generated comparison against the fastest option: *"2.5h longer than the
fastest option, but 1 fewer transfer and no overnight airport wait."*

**Why.** A ranking a user cannot interrogate is a ranking they will not trust. It is
also the cheapest debugging tool in the system — when a profile picks something odd, the
breakdown says exactly which weight did it.

---

## 5. Store UTC, render two local clocks

**Decision.** All instants are UTC internally. The timeline renders the START in the
origin's local clock and the END in the destination's, each tagged with its offset and
place, plus explicit `timezone change` / `date changes` markers and an arrival-day
offset.

**Rejected: rendering everything in the user's local time.** It is technically simple
and practically useless: a Peru arrival shown in Istanbul time has misled the traveller
about the day they arrive.

**Simplification accepted.** Locations carry fixed UTC offsets rather than IANA zones —
correct for this demo world, wrong for DST boundaries. It is on the roadmap and called
out in [limitations](limitations.md) rather than hidden.

---

## 6. Deterministic mocks instead of random ones

**Decision.** No `Random`, no wall clock. Mock fares jitter through a stable FNV-1a hash
of (route, date), so the same request yields identical output on every machine forever.

**Why.** It makes the end-to-end test meaningful (it can assert real numbers, not just
"something came back"), it makes screenshots reproducible via `tools/screenshots.mjs`,
and it removes the single most common source of flaky test suites.

**Rejected: recorded HTTP fixtures.** Heavier, and they rot silently when the shape
changes; hand-written mock adapters keep the canonical boundary honest instead.

---

## 7. Fail-soft per candidate, not per search

**Decision.** A provider that throws removes the candidates that depended on it and the
search continues with the rest. A test drives this with an access provider that fails
for exactly one airport.

**Why.** In a real travel system, providers are down constantly. A search that returns
three good options instead of five is a degraded answer; a search that returns an error
because one hotel API timed out is a broken product.

**Boundary.** "No route at all" is still a real failure and surfaces as `422` with a
machine-readable reason — degraded is not the same as silent.

---

## 8. In-memory trip store in the showcase

**Decision.** `ITripStore` is an interface with an in-memory implementation. No database,
no migrations, no container to run.

**Why.** The interesting engineering here is composition and scoring. A recruiter should
be able to clone and run in under a minute; a Postgres dependency would cost that for
zero demonstrative value. The seam is there, so persistence is an implementation, not a
rewrite.

---

## 9. Mock data that is honest about being mock

**Decision.** Every warning carries `IsDemoData`, the UI shows a persistent badge plus a
per-warning chip, and the README says it twice.

**Why.** Presenting invented visa rules as advice would be both misleading and, for a
portfolio, a red flag about judgement. Being loud about what is simulated costs nothing
and signals exactly the discipline a travel-domain team needs.

---

## 10. A clean re-implementation rather than a copy of the private system

**Context.** This showcase was extracted from a larger private product.

**Decision.** Nothing was copied. The composition core was rewritten for the public repo
against mock providers, with a fresh git history.

**Why.** Copying files from a system with live deployment configuration is how secrets
and infrastructure details leak. Rewriting made leakage structurally impossible, and it
let the scoring and timeline appear in their sharpest form rather than entangled with
persistence and live-provider concerns.
