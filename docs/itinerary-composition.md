# Itinerary composition

`ItineraryComposer.ComposeAsync` builds full door-to-door chains for
Beykoz to Machu Picchu (and any pair the mock world can serve):

1. **Resolve** origin/destination via `IPlaceResolver` (unknown gives a
   `NoRouteFoundException` with a machine-readable reason).
2. **Gateways**: nearest airports each side (`IST` + `SAW` origin-side, `CUZ`
   destination-side).
3. **Flight paths**: BFS over the flight network (`DestinationsFromAsync`), up to 4
   legs, pruned when a hop moves >500 km away from the target. The demo network yields
   `IST->MAD->LIM->CUZ`, `IST->BOG->LIM->CUZ`, `SAW->MAD->LIM->CUZ`.
4. **Ground approaches**: DFS over scheduled legs from the arrival airport to the
   destination — the rail route (`CUZ->Cusco->Ollantaytambo->Aguas Calientes->Machu
   Picchu`) and the budget trek (`...->Hidroelectrica->(walk)->Aguas Calientes->...`).
5. **Candidate grid**: gateway x path x approach x access ride (taxi/shuttle) x
   overnight flavour (hotel vs airport wait), capped and deduped.
6. **Per-candidate build**: sequencing with real buffers — check-in/security
   checkpoint (120 min intl / 75 min domestic, less on connections), passport control
   after border flights, baggage re-check on separate tickets, connection waits, a
   **gate-town hotel night** timed so the final shuttle/walk arrives before the 07:00
   entry slot, then the destination activity.
7. **Fail-soft**: any provider exception kills only the candidate (or gateway branch)
   being built. One test drives this with an access provider that throws for SAW.
8. **Hard preferences**: max transfers / avoid overnight layovers / reduced walking
   filter the set but degrade gracefully (never to zero candidates).

Totals per candidate: converted price (travellers x fares; hotel rooms per 2 people),
door-to-gate duration, transfers, walking/waiting minutes, overnight airport waits,
risk (connection tightness + self-transfers + borders), comfort, and a CO2e estimate
from Haversine distance x per-mode factors.
