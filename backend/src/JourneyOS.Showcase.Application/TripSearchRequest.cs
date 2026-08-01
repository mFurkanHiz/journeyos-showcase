namespace JourneyOS.Showcase.Application;

/// <summary>Everything a search can express. Preferences filter the candidate set
/// during composition; they do NOT alter the scoring weights, which stay fixed per
/// profile so a score means the same thing across searches (docs/scoring.md). A
/// preference no candidate satisfies is relaxed and reported, never silently dropped.</summary>
public sealed record TripSearchRequest
{
    public required string Origin { get; init; }
    public required string Destination { get; init; }
    public required DateOnly DepartureDate { get; init; }
    public DateOnly? ReturnDate { get; init; }
    public int Travellers { get; init; } = 1;
    public string Currency { get; init; } = "USD";

    /// <summary>Cap on transfers; candidates above it are dropped (null = no cap).
    /// Relaxed and reported if nothing meets the cap.</summary>
    public int? MaxTransfers { get; init; }
    /// <summary>Drops candidates that park the traveller in an airport overnight.
    /// Relaxed and reported if every candidate does.</summary>
    public bool AvoidOvernightLayovers { get; init; }
    /// <summary>Accessibility: drops candidates with more than 30 minutes on foot.
    /// Relaxed and reported if no approach is that short.</summary>
    public bool ReducedWalking { get; init; }
}
