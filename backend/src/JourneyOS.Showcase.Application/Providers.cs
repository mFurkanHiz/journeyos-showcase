using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Application;

/// <summary>The provider seams. Every implementation — the deterministic mocks in
/// this showcase, or a real GDS/rail/hotel API in the private system — returns
/// CANONICAL domain types only. Swapping a mock for a live adapter is a DI change,
/// not a domain change (docs/provider-adapters.md).</summary>
public interface IPlaceResolver
{
    /// <summary>Free text ("Beykoz, Istanbul") → a known node, or null.</summary>
    Task<TransportNode?> ResolveAsync(string query, CancellationToken ct = default);
}

public interface IGeocodingProvider
{
    Task<Location?> GeocodeAsync(string query, CancellationToken ct = default);
}

public interface IGatewayDiscoveryService
{
    /// <summary>Nearest boardable airports to a place, closest first.</summary>
    Task<IReadOnlyList<TransportNode>> FindGatewaysAsync(Location near, int max = 3, CancellationToken ct = default);
}

public interface IAirportAccessProvider
{
    /// <summary>Door↔airport options (taxi, shuttle, urban rail) for one direction.</summary>
    Task<IReadOnlyList<TransportOffer>> GetAccessAsync(
        TransportNode place, TransportNode airport, DateTime earliestDepartUtc, CancellationToken ct = default);
}

public interface IUrbanRoutingProvider
{
    /// <summary>Inside-one-city movement between two nodes (airport→centre etc.).</summary>
    Task<IReadOnlyList<TransportOffer>> RouteAsync(
        TransportNode from, TransportNode to, DateTime earliestDepartUtc, CancellationToken ct = default);
}

public interface IPublicTransportProvider
{
    /// <summary>Scheduled intercity ground/water legs (train, bus, ferry, walk-connectors)
    /// departing a node after the given time.</summary>
    Task<IReadOnlyList<TransportOffer>> DeparturesAsync(
        TransportNode from, DateTime earliestDepartUtc, CancellationToken ct = default);
}

public interface IFlightSearchProvider
{
    /// <summary>Direct flights between two airports on a date (the composer builds
    /// multi-leg paths itself, so connections stay visible and scoreable).</summary>
    Task<IReadOnlyList<TransportOffer>> SearchAsync(
        TransportNode from, TransportNode to, DateOnly date, CancellationToken ct = default);

    /// <summary>Airports reachable nonstop from this one — the composer's path search
    /// expands through these.</summary>
    Task<IReadOnlyList<TransportNode>> DestinationsFromAsync(TransportNode from, CancellationToken ct = default);
}

public interface IHotelSearchProvider
{
    Task<IReadOnlyList<StayOffer>> SearchAsync(TransportNode near, DateOnly night, CancellationToken ct = default);
}

public interface IActivitySearchProvider
{
    Task<IReadOnlyList<ActivityOffer>> SearchAsync(TransportNode at, DateOnly date, CancellationToken ct = default);
}

/// <summary>Country-pair entry rules + health notes. In this showcase the data is
/// EXPLICITLY demo data (every Warning carries IsDemoData) — the private system
/// layers live government feeds behind the same seam.</summary>
public interface IRequirementWarningProvider
{
    Task<IReadOnlyList<Warning>> ForRouteAsync(
        string nationalityCountry, IReadOnlyList<string> visitedCountries, CancellationToken ct = default);
}

/// <summary>Fixed-rate demo currency conversion (docs/limitations.md).</summary>
public interface ICurrencyConverter
{
    bool Supports(string currency);
    Money Convert(Money amount, string toCurrency);
}

public interface ITripStore
{
    void Save(Trip trip);
    Trip? Get(Guid id);
}
