# Canonical model

External provider DTOs never cross into Application or Domain. Each adapter defines
its own **private** wire records and normalizes them at the boundary.

From `MockFlightProvider` (Infrastructure):

```csharp
// The pretend GDS wire format — what a real flight API would send.
private sealed record WireFlight(
    string dep_iata, string arr_iata, string dep_local, string arr_local,
    string flight_date, int fare_usd_cents, string marketing_carrier, bool low_cost);

// Normalization boundary: wire -> canonical
IReadOnlyList<TransportOffer> canonical = wire.Select(w => new TransportOffer(
    ProviderName: "MockFlightProvider",
    Mode: /* domestic vs international from country codes */,
    From: ..., To: ...,
    DepartUtc: /* local + fixed offset -> UTC */,
    ArriveUtc: ...,
    Price: new Money(w.fare_usd_cents / 100m, "USD"),
    Carrier: w.marketing_carrier,
    IsSeparateTicket: w.low_cost)).ToList();
```

What normalization guarantees:

- **UTC everywhere inside the engine.** Wire formats speak local clock strings; the
  canonical offer speaks UTC. Local rendering happens only at the timeline edge.
- **Money is explicit.** Cents-as-int, strings, per-leg currencies all become `Money`.
- **Semantics are canonical.** "low_cost" (a provider notion) becomes
  `IsSeparateTicket` (a composition notion the warning engine understands).
- **Tested.** `NormalizationTests` assert a 07:55 Istanbul departure surfaces as
  04:55 UTC with the right mode, currency and provider name.
