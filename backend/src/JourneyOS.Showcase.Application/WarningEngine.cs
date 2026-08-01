using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Application;

/// <summary>Rule-based warnings over a composed itinerary + country-pair notes from
/// the requirement provider. EVERYTHING here is demo data (Warning.IsDemoData) —
/// the UI and README repeat that. The private system feeds the same seam from live
/// government sources; the RULES are the showcase, not the facts.</summary>
public sealed class WarningEngine
{
    private readonly IRequirementWarningProvider _requirements;
    public WarningEngine(IRequirementWarningProvider requirements) => _requirements = requirements;

    public async Task ApplyAsync(Itinerary itinerary, string nationalityCountry, CancellationToken ct = default)
    {
        var w = itinerary.Warnings;
        var segs = itinerary.Segments;

        // Country-pair rules (visa / transit / health / passport) from the provider.
        var visited = segs.SelectMany(s => new[] { s.From.Location.CountryCode, s.To.Location.CountryCode })
            .Distinct().Where(c => c != nationalityCountry).ToList();
        try
        {
            w.AddRange(await _requirements.ForRouteAsync(nationalityCountry, visited, ct));
        }
        catch
        {
            w.Add(new Warning("requirements_unavailable", WarningSeverity.Info,
                "Entry requirements unavailable",
                "The requirement provider failed — check documents independently. (Demo)"));
        }

        // Segment-derived rules.
        for (var i = 0; i < itinerary.Transfers.Count; i++)
        {
            var t = itinerary.Transfers[i];
            if (t.WaitMinutes < 45)
                w.Add(new Warning("short_connection", WarningSeverity.Caution,
                    $"Short connection at {t.At.Name}",
                    $"Only {t.WaitMinutes} min to make the next departure — a small delay breaks the chain."));
            if (t.IsSelfTransfer)
                w.Add(new Warning("self_transfer", WarningSeverity.Caution,
                    $"Self-transfer at {t.At.Name}",
                    "Separate tickets: a missed connection is NOT protected by the carrier."));
            if (t.RequiresBaggageRecheck)
                w.Add(new Warning("baggage_recheck", WarningSeverity.Info,
                    $"Baggage re-check at {t.At.Name}",
                    "Collect bags and check them in again between these tickets."));
            if (t.IsOvernight && t.At.Kind == NodeKind.Airport && t.WaitMinutes >= 300)
                w.Add(new Warning("overnight_airport", WarningSeverity.Caution,
                    $"Overnight wait at {t.At.Name}",
                    $"~{t.WaitMinutes / 60}h at the airport across the night hours."));
            if (t.WaitMinutes is >= 45 and < 60 && t.At.Kind == NodeKind.Airport)
                w.Add(new Warning("risky_connection", WarningSeverity.Info,
                    $"Tight-but-legal connection at {t.At.Name}",
                    "Within rules, yet leaves little slack for delays."));
        }

        foreach (var s in segs)
        {
            if (s.Mode == SegmentMode.Walking && s.DurationMinutes >= 60)
                w.Add(new Warning("long_walk", WarningSeverity.Caution,
                    $"Long walk: {s.From.Name} → {s.To.Name}",
                    $"~{s.DurationMinutes} min on foot with luggage. Not suitable for reduced mobility."));
            if (s.CrossesBorder && !s.IsFlight)
                w.Add(new Warning("border_crossing", WarningSeverity.Info,
                    $"Land border: {s.From.Location.CountryCode} → {s.To.Location.CountryCode}",
                    "Expect document checks and possible queues."));
        }

        // Airport change inside one metro area (e.g. land IST, depart SAW).
        var flightSegs = segs.Where(s => s.IsFlight).ToList();
        for (var i = 0; i + 1 < flightSegs.Count; i++)
            if (flightSegs[i].To.Code != flightSegs[i + 1].From.Code &&
                Haversine.DistanceKm(flightSegs[i].To.Location, flightSegs[i + 1].From.Location) < 120)
                w.Add(new Warning("airport_change", WarningSeverity.Caution,
                    $"Airport change: {flightSegs[i].To.Code} → {flightSegs[i + 1].From.Code}",
                    "You must travel between two airports in the same city to continue."));

        // Seasonal note at the destination (pure demo rule).
        var arrival = segs.Last(s => s.Mode == SegmentMode.Activity);
        var month = arrival.To.Location.ToLocal(arrival.DepartUtc).Month;
        if (arrival.To.Code == "MACHU" && month == 2)
            w.Add(new Warning("seasonal_closure", WarningSeverity.Caution,
                "Inca Trail closed in February (demo note)",
                "The classic trek closes for maintenance each February; the citadel itself stays open."));
    }
}
