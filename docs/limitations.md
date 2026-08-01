# Known limitations

Deliberate scope cuts — each with the reason and the intended fix (see roadmap.md).
Nothing here is hidden; the demo is honest about what it is.

| Limitation | Why | Fix |
|---|---|---|
| **Outbound only.** `returnDate` is accepted but the return leg is not composed. | Keeps the demo focused on the hard part (multimodal composition) without doubling every candidate. | Compose the return symmetrically; roadmap. |
| **Fixed UTC offsets**, no IANA/DST. | The demo world spans a fixed date; correct offsets are enough to prove UTC-vs-local rendering. | `TimeZoneInfo` per location; roadmap. |
| **In-memory trip store.** Restarting the API forgets trips. | Persistence is orthogonal to the composition showcase; the private system uses EF Core. | Pluggable `ITripStore`; roadmap. |
| **Small demo world** — 12 nodes, 6 flight routes, a handful of ground legs. | Enough to produce genuinely different Beykoz to Machu Picchu itineraries and cover every segment mode. | Load a wider dataset behind the same providers. |
| **Prices/schedules are deterministic mocks.** | This is a portfolio demo, not a booking engine; determinism makes tests reliable. | Real adapters behind the same seams. |
| **Warnings are demo rules.** | Never present unofficial data as advice. | Live advisory feed behind `IRequirementWarningProvider`. |
| **No auth / accounts / persistence UI.** | Out of showcase scope; exists in the private system. | Not planned for the showcase. |
| **One activity per destination.** | The demo needs a single anchor (the citadel entry). | More activities per node. |
| **Walking-vs-vehicle heuristic.** | A small rule ("ride if a vehicle leaves within 90 min") stands in for real preference modelling. | Preference-weighted leg choice. |
