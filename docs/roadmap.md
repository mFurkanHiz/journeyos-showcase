# Roadmap

Ordered by how much each would strengthen the showcase, not by effort.

1. **Return-leg composition.** Compose the inbound journey symmetrically and score the
   round trip as one unit.
2. **Real timezones.** Replace fixed UTC offsets with `TimeZoneInfo` so DST and
   half-hour zones render correctly.
3. **Pluggable persistence.** A durable `ITripStore` (SQLite/EF) so trips survive
   restarts and can be shared by id — mirrors the private system.
4. **A live adapter, end to end.** Implement one real provider (e.g. an open flight or
   rail data source) behind its existing seam to prove the mock-to-live swap on real
   data.
5. **Wider demo world.** More nodes, routes and activities loaded from a data file so
   the engine serves many origin/destination pairs.
6. **Preference-weighted leg choice.** Replace the walking-vs-vehicle heuristic with a
   proper per-traveller preference model.
7. **i18n.** The private system stores translations as data; the showcase could expose
   a slim version of that.

None of these change the architecture — they slot behind the seams that already exist.
