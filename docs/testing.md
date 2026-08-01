# Testing

30 tests in `backend/tests/JourneyOS.Showcase.Tests`, three layers:

## Unit
- **Haversine** — known great-circle distances (IST-MAD, LIM-CUZ, zero).
- **Normalization** — flight/ground wire DTOs map to canonical UTC offers with the
  right mode/currency/provider; a 07:55 local departure becomes 04:55 UTC.
- **Composer** — multiple distinct candidates; space + time continuity of segments;
  no duplicate segments; the door-to-door mode spectrum is covered; determinism
  (same request twice, identical totals); a failing provider removes only its
  candidates; unknown route gives a meaningful error; preferences filter the set.
- **Time zones** — Istanbul (+03:00) and Lima (-05:00) clocks render with their own
  offsets; date rollover and arrival-day offset are reported.
- **Scoring** — weights per profile sum to 1; normalization stays in [0,1] and flat
  metrics do not punish; Fastest picks min duration; Cheapest picks min price;
  Balanced applies weights (hand-computed winner); explanation compares to fastest.
- **Warnings** — visa/health/passport on the canonical route (all demo-flagged);
  short connection; overnight airport; self-transfer + airport change + baggage
  re-check; long walk + seasonal closure.

## Integration (WebApplicationFactory)
Real HTTP through the full pipeline: `/health`; search returns three profiles; trip /
itineraries / itinerary-detail / comparison round-trip; bad input is 400, unknown
trip is 404, no-route is 422 with a machine-readable error.

## End-to-end
The canonical Beykoz to Machu Picchu demo over HTTP: profile picks honour their own
definitions across the stored winners, currency is respected, every winner is a real
door-to-door chain (international + domestic flight + hotel + activity + train or bus)
with a continuous, monotonic timeline and a non-empty warning set.

## Run

```bash
dotnet test JourneyOS.Showcase.slnx
# with coverage:
dotnet test JourneyOS.Showcase.slnx --collect:"XPlat Code Coverage"
```

Determinism strategy: no `Random`, no `DateTime.Now`. Mock prices jitter through a
stable FNV-1a hash of (route, date), so a fare is identical on every machine and run.
