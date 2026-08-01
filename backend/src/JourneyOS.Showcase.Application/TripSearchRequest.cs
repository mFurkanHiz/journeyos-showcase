namespace JourneyOS.Showcase.Application;

/// <summary>Everything a search can express. Preferences influence composition
/// (candidate filtering) and scoring (weight nudges) — see docs/scoring.md.</summary>
public sealed record TripSearchRequest
{
    public required string Origin { get; init; }
    public required string Destination { get; init; }
    public required DateOnly DepartureDate { get; init; }
    public DateOnly? ReturnDate { get; init; }
    public int Travellers { get; init; } = 1;
    public string Currency { get; init; } = "USD";

    /// <summary>Hard cap on transfers; candidates above it are dropped (null = no cap).</summary>
    public int? MaxTransfers { get; init; }
    /// <summary>Filters out candidates that park the traveller in an airport overnight
    /// when at least one alternative avoids it; otherwise becomes a scoring penalty.</summary>
    public bool AvoidOvernightLayovers { get; init; }
    /// <summary>Accessibility: prefer candidates with little walking; long walking
    /// segments are excluded when an alternative approach exists.</summary>
    public bool ReducedWalking { get; init; }
}
