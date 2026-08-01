namespace JourneyOS.Showcase.Domain;

/// <summary>Every way a traveller can move (or pause) on a door-to-door journey.
/// The point of JourneyOS: a trip is not a flight — it is a chain of these.</summary>
public enum SegmentMode
{
    Walking,
    Taxi,
    PrivateTransfer,
    UrbanTransit,
    IntercityBus,
    Train,
    Ferry,
    DomesticFlight,
    InternationalFlight,
    AirportAccess,
    HotelStay,
    Wait,
    Activity,
    Checkpoint,
}

public enum OptimizationProfile { Fastest, Cheapest, Balanced }

public enum WarningSeverity { Info, Caution, Critical }

/// <summary>A geographic place with its clock. UTC offsets are FIXED per location in
/// this showcase (a deliberate simplification over full IANA/DST handling — see
/// docs/limitations.md); they are enough to demonstrate correct UTC↔local rendering
/// and date rollovers across an Istanbul→Peru trip.</summary>
public sealed record Location(string Name, string CountryCode, double Lat, double Lon, int UtcOffsetMinutes)
{
    public string UtcOffsetLabel =>
        $"UTC{(UtcOffsetMinutes >= 0 ? "+" : "-")}{Math.Abs(UtcOffsetMinutes) / 60:00}:{Math.Abs(UtcOffsetMinutes) % 60:00}";

    public DateTime ToLocal(DateTime utc) => utc.AddMinutes(UtcOffsetMinutes);
}

public enum NodeKind { Place, Airport, Station, BusStop, Port, Hotel, Attraction }

/// <summary>A boardable/leavable point on the network: an airport, a rail station,
/// a trailhead — or just the traveller's neighbourhood.</summary>
public sealed record TransportNode(string Code, string Name, NodeKind Kind, Location Location);

public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Plus(Money other) =>
        other.Currency == Currency
            ? this with { Amount = Amount + other.Amount }
            : throw new InvalidOperationException(
                $"Cannot add {other.Currency} to {Currency} without conversion — convert first.");

    public override string ToString() => $"{Amount:0.##} {Currency}";
}

/// <summary>One leg of the composed journey. Times are stored in UTC; the endpoints
/// carry their own offsets so every renderer can show both clocks.</summary>
public sealed class ItinerarySegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required SegmentMode Mode { get; init; }
    public required TransportNode From { get; init; }
    public required TransportNode To { get; init; }
    public required DateTime DepartUtc { get; init; }
    public required DateTime ArriveUtc { get; init; }
    public Money? Price { get; init; }
    public string? Carrier { get; init; }
    public string? Note { get; init; }
    /// <summary>True when this leg is sold separately from the previous flight —
    /// a missed connection is the traveller's problem (self-transfer).</summary>
    public bool IsSeparateTicket { get; init; }

    public int DurationMinutes => (int)Math.Round((ArriveUtc - DepartUtc).TotalMinutes);
    public bool CrossesBorder => From.Location.CountryCode != To.Location.CountryCode;
    public bool IsFlight => Mode is SegmentMode.DomesticFlight or SegmentMode.InternationalFlight;
}

/// <summary>The joint between two consecutive transport segments: how long the
/// traveller waits, where, and how risky the hand-off is.</summary>
public sealed record Transfer(
    TransportNode At,
    DateTime FromUtc,
    DateTime ToUtc,
    bool IsSelfTransfer,
    bool RequiresBaggageRecheck,
    bool IsOvernight)
{
    public int WaitMinutes => (int)Math.Round((ToUtc - FromUtc).TotalMinutes);
}

public sealed record Warning(
    string Code,
    WarningSeverity Severity,
    string Title,
    string Detail,
    int? SegmentIndex = null)
{
    /// <summary>Every warning in this showcase is demo/mock data — surfaced on the
    /// record itself so no consumer can accidentally present it as official advice.</summary>
    public bool IsDemoData => true;
}

/// <summary>One scored component of an itinerary (e.g. price), already normalized
/// against the candidate set and weighted by the active profile.</summary>
public sealed record ScoreComponent(string Name, double Normalized, double Weight)
{
    public double Weighted => Math.Round(Normalized * Weight, 4);
}

public sealed record ScoreBreakdown(
    OptimizationProfile Profile,
    IReadOnlyList<ScoreComponent> Components,
    string Explanation)
{
    public double Total => Math.Round(Components.Sum(c => c.Weighted), 4);
}

/// <summary>A fully composed door-to-door alternative with everything a traveller
/// (and the scoring engine) needs to judge it.</summary>
public sealed class Itinerary
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Label { get; init; }
    public required IReadOnlyList<ItinerarySegment> Segments { get; init; }
    public required IReadOnlyList<Transfer> Transfers { get; init; }

    public required Money TotalPrice { get; init; }
    public required int TotalDurationMinutes { get; init; }
    public required int TransferCount { get; init; }
    public required int WalkingMinutes { get; init; }
    public required int WaitingMinutes { get; init; }
    public required int OvernightWaits { get; init; }
    /// <summary>0 (safe) .. 1 (fragile): connection tightness, self-transfers, borders.</summary>
    public required double RiskScore { get; init; }
    /// <summary>0 (grim) .. 1 (pleasant): fewer hops, less walking, real beds at night.</summary>
    public required double ComfortScore { get; init; }
    public required double CarbonKgEstimate { get; init; }
    public required IReadOnlyList<string> RequiredDocuments { get; init; }

    public List<Warning> Warnings { get; } = new();
    public ScoreBreakdown? Score { get; set; }

    public DateTime DepartUtc => Segments[0].DepartUtc;
    public DateTime ArriveUtc => Segments[^1].ArriveUtc;
}

/// <summary>A search and its composed results — what the API stores and serves.</summary>
public sealed class Trip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string OriginQuery { get; init; }
    public required string DestinationQuery { get; init; }
    public required DateOnly DepartureDate { get; init; }
    public DateOnly? ReturnDate { get; init; }
    public required int Travellers { get; init; }
    public required string Currency { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public required IReadOnlyList<Itinerary> Itineraries { get; init; }
    /// <summary>Which itinerary each profile chose. One itinerary may win several
    /// profiles when it dominates the candidate set — honest, not a bug.</summary>
    public required IReadOnlyDictionary<OptimizationProfile, Guid> ProfilePicks { get; init; }

    /// <summary>Preferences the traveller asked for that no candidate could satisfy.
    /// Empty on a normal search; non-empty means these results knowingly violate part
    /// of the request, and every consumer is expected to say so.</summary>
    public IReadOnlyList<RelaxedPreference> RelaxedPreferences { get; init; } = [];
}

/// <summary>A preference the traveller asked for that NO candidate could satisfy.
/// The composer relaxes it rather than returning nothing — but it records it, because
/// quietly returning results that violate a ticked checkbox is the kind of dishonesty
/// this codebase exists to avoid.</summary>
public sealed record RelaxedPreference(string Preference, string Reason);
