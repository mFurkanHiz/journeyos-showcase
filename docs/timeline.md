# Timeline

`TimelineBuilder` renders a composed itinerary for humans without ever mixing clocks:

- **Storage is UTC.** Every segment stores `DepartUtc` / `ArriveUtc`.
- **Rendering is dual-local.** Each timeline entry prints the START in the origin
  node local clock and the END in the destination node local clock, each tagged with
  its offset and place: `09:40 (UTC+03:00, Istanbul Airport) -> 17:15 (UTC-05:00,
  Lima Jorge Chavez)`.
- **Explicit flags** per entry: `crossesTimeZone`, `dateChanges`.
- **Arrival day offset**: destination local date minus departure local date — the
  Beykoz to Machu Picchu demo arrives +2 days, and a test asserts it is at least 1.
- Buffers (check-in, security, passport, baggage re-check), waits, hotel nights and
  the activity are first-class entries, so the timeline is gap-free by construction —
  an integration test walks consecutive entries asserting monotonic start times.

Simplification: locations carry **fixed UTC offsets** (no IANA / DST). This is correct
for the demo world and is documented as a limitation with the intended fix
(see limitations.md).
