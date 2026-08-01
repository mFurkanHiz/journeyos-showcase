using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Application;

/// <summary>The one entry point the API calls: compose the candidate space, score
/// it per profile, attach warnings, persist the trip.</summary>
public sealed class TripService
{
    /// <summary>Demo nationality for requirement rules — the showcase has no user
    /// accounts, so the canonical traveller is Turkish (matching the demo route).</summary>
    private const string DemoNationality = "TR";

    private readonly ItineraryComposer _composer;
    private readonly ScoringService _scoring;
    private readonly WarningEngine _warnings;
    private readonly ITripStore _store;

    public TripService(ItineraryComposer composer, ScoringService scoring, WarningEngine warnings, ITripStore store)
    { _composer = composer; _scoring = scoring; _warnings = warnings; _store = store; }

    public async Task<Trip> SearchAsync(TripSearchRequest request, CancellationToken ct = default)
    {
        var composition = await _composer.ComposeAsync(request, ct);
        var winners = _scoring.PickPerProfile(composition.Candidates);

        // The trip stores each profile's winner (deduped — one itinerary can win
        // several profiles when it dominates; the API exposes profile → id mapping).
        var distinct = winners.Values.DistinctBy(i => i.Id).ToList();
        foreach (var itinerary in distinct)
            await _warnings.ApplyAsync(itinerary, DemoNationality, ct);

        var trip = new Trip
        {
            OriginQuery = request.Origin,
            DestinationQuery = request.Destination,
            DepartureDate = request.DepartureDate,
            ReturnDate = request.ReturnDate,
            Travellers = request.Travellers,
            Currency = request.Currency,
            Itineraries = distinct,
            ProfilePicks = winners.ToDictionary(kv => kv.Key, kv => kv.Value.Id),
            // Carried onto the trip so the API — and therefore the UI — has to confront
            // the fact that these results do not fully match what was asked for.
            RelaxedPreferences = composition.Relaxed,
        };
        _store.Save(trip);
        return trip;
    }
}
