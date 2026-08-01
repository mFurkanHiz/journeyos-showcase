# Provider adapters

Ten seams in `JourneyOS.Showcase.Application/Providers.cs`:

| Seam | Duty | Mock implementation |
|---|---|---|
| `IPlaceResolver` | free text to known node | `MockPlaceResolver` |
| `IGeocodingProvider` | free text to coordinates | `MockGeocodingProvider` |
| `IGatewayDiscoveryService` | nearest airports (Haversine) | `MockGatewayDiscovery` |
| `IAirportAccessProvider` | door to/from airport rides | `MockAirportAccessProvider` |
| `IUrbanRoutingProvider` | inside-city hops | `MockUrbanTransportProvider` |
| `IPublicTransportProvider` | scheduled train/bus/walk connectors | `MockPublicTransportProvider` |
| `IFlightSearchProvider` | direct flights + network edges | `MockFlightProvider` |
| `IHotelSearchProvider` | nightly stays near a node | `MockHotelProvider` |
| `IActivitySearchProvider` | destination entries/tickets | `MockActivityProvider` |
| `IRequirementWarningProvider` | country-pair entry rules (DEMO) | `MockWarningProvider` |

Support seams: `ICurrencyConverter` (fixed demo rates), `ITripStore` (in-memory).

## Swapping a mock for a live adapter

Every seam returns **canonical domain types**, so a live integration is one new class
plus one DI line in `Api/Program.cs`:

```csharp
// builder.Services.AddSingleton<IFlightSearchProvider, MockFlightProvider>();
builder.Services.AddSingleton<IFlightSearchProvider, SomeGdsFlightAdapter>();
```

The adapter owns everything provider-specific: HTTP, auth, retries, and mapping the
provider wire format into `TransportOffer`. Nothing else in the system changes —
composer, scoring, timeline, warnings and API are already provider-agnostic. This is
exactly how the private JourneyOS system runs live geocoding / road-routing / travel-
advisory adapters behind the same shape of seams.
