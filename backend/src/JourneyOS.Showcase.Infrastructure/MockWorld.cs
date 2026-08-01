using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Infrastructure;

/// <summary>The deterministic demo planet. Every mock adapter reads from here — no
/// schedule, fare or place is ever hardcoded inside the composer. Same input ⇒ same
/// output, always: prices jitter per (route, date) through a stable FNV-1a hash,
/// never through Random. All of it is invented demo data.</summary>
public static class MockWorld
{
    // ── Places (fixed UTC offsets — see docs/limitations.md) ─────────────────────
    public static readonly TransportNode Beykoz = Place("BEYKOZ", "Beykoz, Istanbul", "TR", 41.125, 29.100, +180);
    public static readonly TransportNode Ist = Airport("IST", "Istanbul Airport", "TR", 41.262, 28.742, +180);
    public static readonly TransportNode Saw = Airport("SAW", "Sabiha Gökçen Airport", "TR", 40.899, 29.309, +180);
    public static readonly TransportNode Mad = Airport("MAD", "Madrid Barajas", "ES", 40.472, -3.561, +60);
    public static readonly TransportNode Bog = Airport("BOG", "Bogotá El Dorado", "CO", 4.702, -74.147, -300);
    public static readonly TransportNode Lim = Airport("LIM", "Lima Jorge Chávez", "PE", -12.022, -77.114, -300);
    public static readonly TransportNode Cuz = Airport("CUZ", "Cusco Airport", "PE", -13.536, -71.939, -300);
    public static readonly TransportNode Cusco = Place("CUSCO", "Cusco", "PE", -13.517, -71.978, -300);
    public static readonly TransportNode Ollanta = Station("OLLANTA", "Ollantaytambo Station", "PE", -13.258, -72.263, -300);
    public static readonly TransportNode Hidro = Station("HIDRO", "Hidroeléctrica", "PE", -13.175, -72.577, -300);
    public static readonly TransportNode Aguas = Place("AGUAS", "Aguas Calientes", "PE", -13.155, -72.525, -300);
    public static readonly TransportNode Machu = Attraction("MACHU", "Machu Picchu", "PE", -13.163, -72.545, -300);

    public static readonly IReadOnlyList<TransportNode> AllNodes =
        new[] { Beykoz, Ist, Saw, Mad, Bog, Lim, Cuz, Cusco, Ollanta, Hidro, Aguas, Machu };

    public static readonly IReadOnlyList<TransportNode> Airports = new[] { Ist, Saw, Mad, Bog, Lim, Cuz };

    // ── Flight network: route → daily departures (local), block time, base USD ───
    public sealed record FlightRoute(
        TransportNode From, TransportNode To, string Carrier, bool SeparateTicket,
        IReadOnlyList<TimeOnly> DailyDepartures, int BlockMinutes, decimal BaseUsd);

    // Fares/times are tuned so the three flight paths form a genuine Pareto set (no
    // single path wins on both price AND time), which is what makes Fastest, Cheapest
    // and Balanced pick different itineraries:
    //   IST→MAD→LIM : cheaper, a bit slower  (the classic connection)
    //   IST→BOG→LIM : faster, pricier        (fewer, longer legs)
    //   SAW→MAD→LIM : cheapest flights but a separate LCC ticket + airport change
    //                 (self-transfer risk), so only Cheapest tolerates it.
    public static readonly IReadOnlyList<FlightRoute> Flights = new[]
    {
        new FlightRoute(Ist, Mad, "Demo Turkish", false, Times("07:55", "15:40"), 280, 235m),
        new FlightRoute(Saw, Mad, "Demo LCC", true, Times("06:30"), 300, 120m),
        new FlightRoute(Ist, Bog, "Demo Andina", false, Times("09:20"), 815, 620m),
        new FlightRoute(Mad, Lim, "Demo Iberoamérica", false, Times("12:05", "23:55"), 800, 440m),
        new FlightRoute(Bog, Lim, "Demo Andina", false, Times("08:15", "17:30"), 175, 265m),
        new FlightRoute(Lim, Cuz, "Demo PeruAir", false, Times("05:40", "09:30", "14:10"), 85, 68m),
    };

    // ── Scheduled ground legs (train / bus / walk connectors / colectivo) ────────
    public sealed record GroundRoute(
        TransportNode From, TransportNode To, SegmentMode Mode, string Carrier,
        IReadOnlyList<TimeOnly> DailyDepartures, int Minutes, decimal Usd);

    public static readonly IReadOnlyList<GroundRoute> Ground = new[]
    {
        new GroundRoute(Cuz, Cusco, SegmentMode.UrbanTransit, "Cusco Colectivo",
            EveryHalfHour("05:00", "22:00"), 30, 1.5m),
        new GroundRoute(Cusco, Ollanta, SegmentMode.IntercityBus, "Valle Sagrado Colectivo",
            Hourly("05:00", "19:00"), 110, 10m),
        new GroundRoute(Ollanta, Aguas, SegmentMode.Train, "Demo PeruRail",
            Times("05:05", "07:45", "12:55", "16:36", "19:00"), 100, 65m),
        new GroundRoute(Cusco, Hidro, SegmentMode.IntercityBus, "Hidro Bus Co.",
            Times("07:30", "13:00"), 330, 15m),
        new GroundRoute(Hidro, Aguas, SegmentMode.Walking, "On foot (rail trail)",
            Hourly("05:00", "14:00"), 165, 0m),
        new GroundRoute(Aguas, Machu, SegmentMode.IntercityBus, "Consettur Shuttle",
            EveryHalfHour("05:30", "15:30"), 25, 12m),
        new GroundRoute(Aguas, Machu, SegmentMode.Walking, "On foot (stone steps)",
            Hourly("04:30", "13:30"), 90, 0m),
    };

    // ── On-demand urban / access rides ───────────────────────────────────────────
    public sealed record OnDemandRide(TransportNode From, TransportNode To, SegmentMode Mode,
        string Carrier, int Minutes, decimal Usd);

    public static readonly IReadOnlyList<OnDemandRide> Rides = new[]
    {
        new OnDemandRide(Beykoz, Ist, SegmentMode.Taxi, "Istanbul Taxi", 50, 34m),
        new OnDemandRide(Beykoz, Ist, SegmentMode.AirportAccess, "Airport Shuttle Line", 80, 4m),
        new OnDemandRide(Beykoz, Saw, SegmentMode.Taxi, "Istanbul Taxi", 55, 30m),
        new OnDemandRide(Beykoz, Saw, SegmentMode.AirportAccess, "Airport Shuttle Line", 90, 4m),
        new OnDemandRide(Cuz, Cusco, SegmentMode.Taxi, "Cusco Taxi", 20, 8m),
    };

    // ── Hotels & activities ──────────────────────────────────────────────────────
    public sealed record Hotel(TransportNode At, string Name, decimal UsdPerNight);

    public static readonly IReadOnlyList<Hotel> Hotels = new[]
    {
        new Hotel(Lim, "Demo Airport Rest Inn", 72m),
        new Hotel(Cusco, "Demo Plaza Cusco", 38m),
        new Hotel(Ollanta, "Demo Valle Lodge", 41m),
        new Hotel(Aguas, "Demo Andes Lodge", 45m),
    };

    public sealed record ActivityDef(TransportNode At, string Title, int Minutes, decimal Usd);

    public static readonly IReadOnlyList<ActivityDef> Activities = new[]
    {
        new ActivityDef(Machu, "Machu Picchu citadel entry (demo ticket)", 180, 52m),
    };

    // ── Deterministic jitter (NO Random anywhere) ────────────────────────────────
    /// <summary>Stable FNV-1a over a key → 0..(spread-1). Same key, same jitter, on
    /// every machine, forever — the whole test suite leans on this.</summary>
    public static int Jitter(string key, int spread)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in key) { h ^= c; h *= 16777619u; }
            return (int)(h % (uint)spread);
        }
    }

    public static DateTime LocalToUtc(TransportNode node, DateOnly date, TimeOnly local) =>
        DateTime.SpecifyKind(date.ToDateTime(local), DateTimeKind.Utc)
            .AddMinutes(-node.Location.UtcOffsetMinutes);

    private static TransportNode Place(string code, string name, string cc, double lat, double lon, int off) =>
        new(code, name, NodeKind.Place, new Location(name, cc, lat, lon, off));
    private static TransportNode Airport(string code, string name, string cc, double lat, double lon, int off) =>
        new(code, name, NodeKind.Airport, new Location(name, cc, lat, lon, off));
    private static TransportNode Station(string code, string name, string cc, double lat, double lon, int off) =>
        new(code, name, NodeKind.Station, new Location(name, cc, lat, lon, off));
    private static TransportNode Attraction(string code, string name, string cc, double lat, double lon, int off) =>
        new(code, name, NodeKind.Attraction, new Location(name, cc, lat, lon, off));

    private static IReadOnlyList<TimeOnly> Times(params string[] hhmm) =>
        hhmm.Select(TimeOnly.Parse).ToList();
    private static IReadOnlyList<TimeOnly> Hourly(string from, string to)
    {
        var start = TimeOnly.Parse(from);
        var end = TimeOnly.Parse(to);
        var list = new List<TimeOnly>();
        for (var t = start; t <= end; t = t.AddHours(1)) list.Add(t);
        return list;
    }
    private static IReadOnlyList<TimeOnly> EveryHalfHour(string from, string to)
    {
        var start = TimeOnly.Parse(from);
        var end = TimeOnly.Parse(to);
        var list = new List<TimeOnly>();
        for (var t = start; t <= end; t = t.AddMinutes(30)) list.Add(t);
        return list;
    }
}
