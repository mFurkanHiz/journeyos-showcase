# Architecture

Four projects, strict inward dependencies — the same layering the private JourneyOS system uses:

```mermaid
flowchart LR
  Api[Api: minimal API + DTO mapping] --> Infra[Infrastructure: mock adapters + in-memory store]
  Infra --> App[Application: provider seams, composer, scoring, timeline, warnings]
  App --> Domain[Domain: Trip, Itinerary, Segment, Transfer, Money, Warning, canonical offers]
```

## Request flow

```mermaid
sequenceDiagram
  participant C as Client
  participant A as Api
  participant K as ItineraryComposer
  participant P as Providers (10 seams)
  participant S as ScoringService
  participant W as WarningEngine
  C->>A: POST /api/trips/search
  A->>K: ComposeAsync(request)
  K->>P: resolve, gateways, flights, ground, hotels, activity
  K-->>A: candidate itineraries (deduped, preference-filtered)
  A->>S: PickPerProfile(candidates)
  S-->>A: Fastest / Cheapest / Balanced + score breakdowns
  A->>W: ApplyAsync(winner) per distinct winner
  A-->>C: Trip (stored in-memory) with profile picks
```

Key decisions:

- **Candidate space, not one answer.** The composer enumerates real alternatives
  (gateway × flight path × ground approach × access mode × overnight flavour) so the
  scorer has genuine trade-offs to choose between.
- **Canonical model boundary.** Provider adapters normalize their private wire DTOs
  into `TransportOffer`/`StayOffer`/`ActivityOffer` before anything else sees them.
- **Fail-soft composition.** A throwing provider removes its candidates, never the search.
- **No persistence by design.** Trips live in an in-memory store; the private system
  uses EF Core — out of scope here (see limitations.md).
