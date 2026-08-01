using JourneyOS.Showcase.Application;
using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Infrastructure;

/// <summary>Each adapter below simulates a REAL integration's shape: it first
/// produces a provider-specific "wire" record (the kind of JSON a GDS / rail API /
/// hotel aggregator would return), then NORMALIZES it into the canonical domain
/// model. The wire records are private to each adapter — nothing outside this file
/// ever sees them. That boundary is the whole point (docs/canonical-model.md), and
/// it is what makes swapping a mock for a live adapter a DI-only change.</summary>
public sealed class MockPlaceResolver : IPlaceResolver
{
    public Task<TransportNode?> ResolveAsync(string query, CancellationToken ct = default)
    {
        var q = query.Trim().ToLowerInvariant();
        var hit = MockWorld.AllNodes.FirstOrDefault(n =>
            n.Code.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
            n.Name.ToLowerInvariant().Contains(q) ||
            q.Contains(n.Name.Split(',')[0].Trim().ToLowerInvariant()));
        return Task.FromResult(hit);
    }
}

public sealed class MockGeocodingProvider : IGeocodingProvider
{
    public async Task<Location?> GeocodeAsync(string query, CancellationToken ct = default) =>
        (await new MockPlaceResolver().ResolveAsync(query, ct))?.Location;
}

public sealed class MockGatewayDiscovery : IGatewayDiscoveryService
{
    public Task<IReadOnlyList<TransportNode>> FindGatewaysAsync(Location near, int max = 3, CancellationToken ct = default)
    {
        IReadOnlyList<TransportNode> result = MockWorld.Airports
            .OrderBy(a => Haversine.DistanceKm(a.Location, near))
            .Take(max)
            .ToList();
        return Task.FromResult(result);
    }
}

public sealed class MockFlightProvider : IFlightSearchProvider
{
    /// <summary>The pretend GDS wire format — what a real flight API would send.</summary>
    private sealed record WireFlight(
        string dep_iata, string arr_iata, string dep_local, string arr_local,
        string flight_date, int fare_usd_cents, string marketing_carrier, bool low_cost);

    public Task<IReadOnlyList<TransportOffer>> SearchAsync(
        TransportNode from, TransportNode to, DateOnly date, CancellationToken ct = default)
    {
        var wire = new List<WireFlight>();
        foreach (var r in MockWorld.Flights.Where(r => r.From.Code == from.Code && r.To.Code == to.Code))
            foreach (var dep in r.DailyDepartures)
            {
                // Deterministic per-(route, date, slot) fare jitter, like real fares move.
                var jitter = MockWorld.Jitter($"{r.From.Code}{r.To.Code}{date:yyyyMMdd}{dep}", 4000);
                var fareCents = (int)(r.BaseUsd * 100) + jitter;
                var arrLocalUtcSide = dep.AddMinutes(r.BlockMinutes
                    + r.From.Location.UtcOffsetMinutes - r.To.Location.UtcOffsetMinutes);
                wire.Add(new WireFlight(r.From.Code, r.To.Code, dep.ToString("HH:mm"),
                    arrLocalUtcSide.ToString("HH:mm"), date.ToString("yyyy-MM-dd"),
                    fareCents, r.Carrier, r.SeparateTicket));
            }

        // ── Normalization boundary: wire → canonical ────────────────────────────
        IReadOnlyList<TransportOffer> canonical = wire.Select(w =>
        {
            var route = MockWorld.Flights.First(r => r.From.Code == w.dep_iata && r.To.Code == w.arr_iata);
            var departUtc = MockWorld.LocalToUtc(route.From, DateOnly.Parse(w.flight_date), TimeOnly.Parse(w.dep_local));
            return new TransportOffer(
                ProviderName: "MockFlightProvider",
                Mode: route.From.Location.CountryCode == route.To.Location.CountryCode
                    ? SegmentMode.DomesticFlight : SegmentMode.InternationalFlight,
                From: route.From, To: route.To,
                DepartUtc: departUtc,
                ArriveUtc: departUtc.AddMinutes(route.BlockMinutes),
                Price: new Money(w.fare_usd_cents / 100m, "USD"),
                Carrier: w.marketing_carrier,
                IsSeparateTicket: w.low_cost);
        }).ToList();
        return Task.FromResult(canonical);
    }

    public Task<IReadOnlyList<TransportNode>> DestinationsFromAsync(TransportNode from, CancellationToken ct = default)
    {
        IReadOnlyList<TransportNode> result = MockWorld.Flights
            .Where(r => r.From.Code == from.Code)
            .Select(r => r.To)
            .DistinctBy(n => n.Code)
            .ToList();
        return Task.FromResult(result);
    }
}

public sealed class MockPublicTransportProvider : IPublicTransportProvider
{
    /// <summary>Pretend rail/bus operator feed (GTFS-flavoured).</summary>
    private sealed record WireDeparture(
        string from_stop, string to_stop, string mode, string operator_name,
        DateTime departure_utc, int travel_minutes, decimal price_usd);

    public Task<IReadOnlyList<TransportOffer>> DeparturesAsync(
        TransportNode from, DateTime earliestDepartUtc, CancellationToken ct = default)
    {
        var floor = earliestDepartUtc == DateTime.MinValue ? DateTime.UnixEpoch : earliestDepartUtc;
        var wire = new List<WireDeparture>();
        foreach (var r in MockWorld.Ground.Where(r => r.From.Code == from.Code))
        {
            // Offer today's and tomorrow's departures relative to the floor.
            var localDate = DateOnly.FromDateTime(r.From.Location.ToLocal(floor));
            for (var d = 0; d < 2; d++)
                foreach (var t in r.DailyDepartures)
                {
                    var utc = MockWorld.LocalToUtc(r.From, localDate.AddDays(d), t);
                    if (utc < floor) continue;
                    wire.Add(new WireDeparture(r.From.Code, r.To.Code, r.Mode.ToString(),
                        r.Carrier, utc, r.Minutes, r.Usd));
                }
        }

        IReadOnlyList<TransportOffer> canonical = wire.Select(w =>
        {
            var route = MockWorld.Ground.First(r =>
                r.From.Code == w.from_stop && r.To.Code == w.to_stop && r.Carrier == w.operator_name);
            return new TransportOffer("MockPublicTransportProvider", route.Mode, route.From, route.To,
                w.departure_utc, w.departure_utc.AddMinutes(w.travel_minutes),
                new Money(w.price_usd, "USD"), w.operator_name);
        }).ToList();
        return Task.FromResult(canonical);
    }
}

public sealed class MockUrbanTransportProvider : IUrbanRoutingProvider
{
    public Task<IReadOnlyList<TransportOffer>> RouteAsync(
        TransportNode from, TransportNode to, DateTime earliestDepartUtc, CancellationToken ct = default)
    {
        IReadOnlyList<TransportOffer> result = MockWorld.Rides
            .Where(r => r.From.Code == from.Code && r.To.Code == to.Code && r.Mode == SegmentMode.Taxi)
            .Select(r => new TransportOffer("MockUrbanTransportProvider", r.Mode, r.From, r.To,
                earliestDepartUtc, earliestDepartUtc.AddMinutes(r.Minutes),
                new Money(r.Usd, "USD"), r.Carrier))
            .ToList();
        return Task.FromResult(result);
    }
}

public sealed class MockAirportAccessProvider : IAirportAccessProvider
{
    public Task<IReadOnlyList<TransportOffer>> GetAccessAsync(
        TransportNode place, TransportNode airport, DateTime earliestDepartUtc, CancellationToken ct = default)
    {
        IReadOnlyList<TransportOffer> result = MockWorld.Rides
            .Where(r => r.From.Code == place.Code && r.To.Code == airport.Code)
            .Select(r => new TransportOffer("MockAirportAccessProvider", r.Mode, r.From, r.To,
                earliestDepartUtc, earliestDepartUtc.AddMinutes(r.Minutes),
                new Money(r.Usd, "USD"), r.Carrier))
            .ToList();
        return Task.FromResult(result);
    }
}

public sealed class MockHotelProvider : IHotelSearchProvider
{
    /// <summary>Pretend hotel-aggregator wire shape.</summary>
    private sealed record WireHotel(string near_code, string property_name, int nightly_usd_cents);

    public Task<IReadOnlyList<StayOffer>> SearchAsync(TransportNode near, DateOnly night, CancellationToken ct = default)
    {
        var wire = MockWorld.Hotels
            .Where(h => h.At.Code == near.Code)
            .Select(h => new WireHotel(h.At.Code, h.Name,
                (int)(h.UsdPerNight * 100) + MockWorld.Jitter($"{h.Name}{night:yyyyMMdd}", 900)))
            .ToList();

        IReadOnlyList<StayOffer> canonical = wire
            .Select(w => new StayOffer("MockHotelProvider",
                MockWorld.AllNodes.First(n => n.Code == w.near_code),
                w.property_name, new Money(w.nightly_usd_cents / 100m, "USD")))
            .ToList();
        return Task.FromResult(canonical);
    }
}

public sealed class MockActivityProvider : IActivitySearchProvider
{
    public Task<IReadOnlyList<ActivityOffer>> SearchAsync(TransportNode at, DateOnly date, CancellationToken ct = default)
    {
        IReadOnlyList<ActivityOffer> result = MockWorld.Activities
            .Where(a => a.At.Code == at.Code)
            .Select(a => new ActivityOffer("MockActivityProvider", a.At, a.Title, a.Minutes,
                new Money(a.Usd, "USD")))
            .ToList();
        return Task.FromResult(result);
    }
}

/// <summary>Country-pair entry rules. Plausible-looking but EXPLICITLY demo data —
/// every Warning carries IsDemoData=true and the UI repeats the disclaimer.</summary>
public sealed class MockWarningProvider : IRequirementWarningProvider
{
    public Task<IReadOnlyList<Warning>> ForRouteAsync(
        string nationalityCountry, IReadOnlyList<string> visitedCountries, CancellationToken ct = default)
    {
        var warnings = new List<Warning>
        {
            new("passport_validity", WarningSeverity.Caution, "Passport validity",
                "Many countries expect 6+ months of passport validity on entry. (Demo note)"),
        };
        if (nationalityCountry == "TR")
        {
            if (visitedCountries.Contains("PE"))
                warnings.Add(new Warning("visa_pe", WarningSeverity.Info, "Peru entry (demo rule)",
                    "Turkish citizens: visa-free for stays up to 90 days in this demo rule set."));
            if (visitedCountries.Contains("ES"))
                warnings.Add(new Warning("transit_es", WarningSeverity.Info, "Madrid transit (demo rule)",
                    "Airside international transit without entering Schengen — no visa in this demo rule set. Verify officially."));
            if (visitedCountries.Contains("CO"))
                warnings.Add(new Warning("transit_co", WarningSeverity.Info, "Bogotá transit (demo rule)",
                    "Airside transit permitted without visa in this demo rule set. Verify officially."));
        }
        if (visitedCountries.Contains("PE"))
            warnings.Add(new Warning("health_pe", WarningSeverity.Info, "Health note (demo)",
                "Yellow-fever vaccination is recommended for Peruvian jungle regions; Machu Picchu itself is highland."));
        return Task.FromResult<IReadOnlyList<Warning>>(warnings);
    }
}

public sealed class DemoCurrencyConverter : ICurrencyConverter
{
    /// <summary>Fixed demo rates (per 1 USD). Not live, never claimed to be.</summary>
    private static readonly Dictionary<string, decimal> PerUsd = new(StringComparer.OrdinalIgnoreCase)
    { ["USD"] = 1m, ["EUR"] = 0.92m, ["TRY"] = 41.5m, ["PEN"] = 3.75m, ["GBP"] = 0.78m };

    public bool Supports(string currency) => PerUsd.ContainsKey(currency);

    public Money Convert(Money amount, string toCurrency)
    {
        if (!PerUsd.TryGetValue(amount.Currency, out var fromRate) ||
            !PerUsd.TryGetValue(toCurrency, out var toRate))
            throw new InvalidOperationException($"Unsupported currency {amount.Currency}->{toCurrency}");
        return new Money(Math.Round(amount.Amount / fromRate * toRate, 2), toCurrency.ToUpperInvariant());
    }
}

public sealed class InMemoryTripStore : ITripStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Trip> _trips = new();
    public void Save(Trip trip) => _trips[trip.Id] = trip;
    public Trip? Get(Guid id) => _trips.TryGetValue(id, out var t) ? t : null;
}
