using JourneyOS.Showcase.Application;
using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Api;

public sealed record SearchRequestDto(
    string Origin,
    string Destination,
    DateOnly DepartureDate,
    DateOnly? ReturnDate = null,
    int Travellers = 1,
    string Currency = "USD",
    int? MaxTransfers = null,
    bool AvoidOvernightLayovers = false,
    bool ReducedWalking = false);

public sealed record SegmentDto(
    string Mode, string From, string FromName, string To, string ToName,
    DateTime DepartUtc, DateTime ArriveUtc, int DurationMinutes,
    decimal? Price, string? Currency, string? Carrier, string? Note,
    bool IsSeparateTicket, bool CrossesBorder);

public sealed record WarningDto(string Code, string Severity, string Title, string Detail, bool IsDemoData);

public sealed record ScoreComponentDto(string Name, double Normalized, double Weight, double Weighted);

public sealed record ScoreDto(string Profile, IReadOnlyList<ScoreComponentDto> Components, double Total, string Explanation);

public sealed record ItinerarySummaryDto(
    Guid Id, string Label, decimal TotalPrice, string Currency,
    int TotalDurationMinutes, int TransferCount, int WalkingMinutes, int WaitingMinutes,
    int OvernightWaits, double RiskScore, double ComfortScore, double CarbonKgEstimate,
    int WarningCount, ScoreDto? Score);

public sealed record ItineraryDetailDto(
    ItinerarySummaryDto Summary,
    IReadOnlyList<SegmentDto> Segments,
    Timeline Timeline,
    IReadOnlyList<WarningDto> Warnings,
    IReadOnlyList<string> RequiredDocuments);

public sealed record RelaxedPreferenceDto(string Preference, string Reason);

public sealed record TripDto(
    Guid Id, string Origin, string Destination, DateOnly DepartureDate, DateOnly? ReturnDate,
    int Travellers, string Currency, DateTime CreatedAtUtc,
    IReadOnlyDictionary<string, Guid> Profiles,
    IReadOnlyList<ItinerarySummaryDto> Itineraries,
    /// <summary>Empty on a normal search. Non-empty means the engine could not honour
    /// part of the request and returned the closest thing instead — clients MUST show it.</summary>
    IReadOnlyList<RelaxedPreferenceDto> RelaxedPreferences);

public sealed record ComparisonRowDto(string Metric, IReadOnlyDictionary<string, string> ByProfile);

public static class ApiMapping
{
    public static TripDto ToDto(this Trip trip) => new(
        trip.Id, trip.OriginQuery, trip.DestinationQuery, trip.DepartureDate, trip.ReturnDate,
        trip.Travellers, trip.Currency, trip.CreatedAtUtc,
        trip.ProfilePicks.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
        trip.Itineraries.Select(i => i.ToSummaryDto()).ToList(),
        trip.RelaxedPreferences.Select(r => new RelaxedPreferenceDto(r.Preference, r.Reason)).ToList());

    public static ItinerarySummaryDto ToSummaryDto(this Itinerary i) => new(
        i.Id, i.Label, i.TotalPrice.Amount, i.TotalPrice.Currency,
        i.TotalDurationMinutes, i.TransferCount, i.WalkingMinutes, i.WaitingMinutes,
        i.OvernightWaits, i.RiskScore, i.ComfortScore, i.CarbonKgEstimate,
        i.Warnings.Count, i.Score?.ToDto());

    public static ScoreDto ToDto(this ScoreBreakdown s) => new(
        s.Profile.ToString(),
        s.Components.Select(c => new ScoreComponentDto(c.Name, c.Normalized, c.Weight, c.Weighted)).ToList(),
        s.Total, s.Explanation);

    public static ItineraryDetailDto ToDetailDto(this Itinerary i, TimelineBuilder timeline) => new(
        i.ToSummaryDto(),
        i.Segments.Select(s => new SegmentDto(
            s.Mode.ToString(), s.From.Code, s.From.Name, s.To.Code, s.To.Name,
            s.DepartUtc, s.ArriveUtc, s.DurationMinutes,
            s.Price?.Amount, s.Price?.Currency, s.Carrier, s.Note,
            s.IsSeparateTicket, s.CrossesBorder)).ToList(),
        timeline.Build(i),
        i.Warnings.Select(w => new WarningDto(w.Code, w.Severity.ToString(), w.Title, w.Detail, w.IsDemoData)).ToList(),
        i.RequiredDocuments);

    public static IReadOnlyList<ComparisonRowDto> ToComparison(this Trip trip)
    {
        var byProfile = trip.ProfilePicks.ToDictionary(
            kv => kv.Key.ToString(),
            kv => trip.Itineraries.First(i => i.Id == kv.Value));
        // These are display strings crossing an English API contract, so they are formatted
        // with the invariant culture — never the server's. On a tr-TR machine the ambient
        // culture renders 0.41 as "0,41", which then ships to every client.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string H(int minutes) => $"{minutes / 60}h {minutes % 60:00}m";
        var rows = new List<(string Metric, Func<Itinerary, string> Get)>
        {
            ("Total price", i => $"{i.TotalPrice.Amount.ToString("0", inv)} {i.TotalPrice.Currency}"),
            ("Door-to-gate duration", i => H(i.TotalDurationMinutes)),
            ("Transfers", i => i.TransferCount.ToString(inv)),
            ("Walking", i => H(i.WalkingMinutes)),
            ("Waiting", i => H(i.WaitingMinutes)),
            ("Overnight airport waits", i => i.OvernightWaits.ToString(inv)),
            ("Connection risk (0-1)", i => i.RiskScore.ToString("0.00", inv)),
            ("Comfort (0-1)", i => i.ComfortScore.ToString("0.00", inv)),
            ("CO₂ estimate (kg)", i => i.CarbonKgEstimate.ToString("0", inv)),
            ("Warnings", i => i.Warnings.Count.ToString(inv)),
        };
        return rows.Select(r => new ComparisonRowDto(
            r.Metric, byProfile.ToDictionary(kv => kv.Key, kv => r.Get(kv.Value)))).ToList();
    }
}
