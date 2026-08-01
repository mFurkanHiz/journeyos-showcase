using JourneyOS.Showcase.Application;
using JourneyOS.Showcase.Domain;
using JourneyOS.Showcase.Infrastructure;

namespace JourneyOS.Showcase.Tests;

public class ScoringTests
{
    /// <summary>A synthetic candidate with controlled metrics — segments are minimal
    /// stubs because scoring reads only the aggregate fields.</summary>
    private static Itinerary Make(string label, decimal price, int durationMin, int transfers,
        double risk = 0.1, int waiting = 60, int overnight = 0, double comfort = 0.8, int walking = 10)
    {
        var node = MockWorld.Beykoz;
        var seg = new ItinerarySegment
        {
            Mode = SegmentMode.Activity, From = node, To = node,
            DepartUtc = new DateTime(2026, 9, 18, 6, 0, 0, DateTimeKind.Utc),
            ArriveUtc = new DateTime(2026, 9, 18, 7, 0, 0, DateTimeKind.Utc),
        };
        return new Itinerary
        {
            Label = label, Segments = new[] { seg }, Transfers = Array.Empty<Transfer>(),
            TotalPrice = new Money(price, "USD"), TotalDurationMinutes = durationMin,
            TransferCount = transfers, WalkingMinutes = walking, WaitingMinutes = waiting,
            OvernightWaits = overnight, RiskScore = risk, ComfortScore = comfort,
            CarbonKgEstimate = 100, RequiredDocuments = Array.Empty<string>(),
        };
    }

    [Fact]
    public void Weights_Sum_To_One_For_Every_Profile()
    {
        foreach (var (profile, weights) in ScoringService.Weights)
            Assert.Equal(1.0, weights.Values.Sum(), 6);
    }

    [Fact]
    public void Normalization_Stays_In_Bounds_And_Flat_Metrics_Do_Not_Punish()
    {
        var candidates = new[]
        {
            Make("a", 900, 2000, 4), Make("b", 1400, 1500, 2), Make("c", 1100, 1800, 4),
        };
        var winners = new ScoringService().PickPerProfile(candidates);
        foreach (var w in winners.Values)
        {
            Assert.NotNull(w.Score);
            Assert.All(w.Score!.Components, c => Assert.InRange(c.Normalized, 0, 1));
            // transfers is flat between the two 4-transfer candidates but has spread
            // overall — bounds are what matter; identical-metric sets score 0.
        }
        var flat = new[] { Make("x", 500, 1000, 3), Make("y", 500, 1200, 3) };
        var flatWinners = new ScoringService().PickPerProfile(flat);
        var priceComponent = flatWinners[OptimizationProfile.Cheapest].Score!
            .Components.First(c => c.Name == "price");
        Assert.Equal(0, priceComponent.Normalized);   // no spread ⇒ no punishment
    }

    [Fact]
    public void Fastest_Picks_Min_Duration_And_Cheapest_Picks_Min_Price()
    {
        var candidates = new[]
        {
            Make("slow-cheap", price: 700, durationMin: 2600, transfers: 3),
            Make("fast-expensive", price: 1900, durationMin: 1500, transfers: 2),
            Make("middle", price: 1200, durationMin: 2000, transfers: 2),
        };
        var winners = new ScoringService().PickPerProfile(candidates);
        Assert.Equal("fast-expensive", winners[OptimizationProfile.Fastest].Label);
        Assert.Equal("slow-cheap", winners[OptimizationProfile.Cheapest].Label);
    }

    [Fact]
    public void Balanced_Applies_Weights_Not_Vibes()
    {
        // Hand-computable set: two extremes and a compromise. With the Balanced
        // weights (price .25, duration .25, transfers .12, risk .12, waiting .08,
        // overnight .08, comfort .06, walking .04) the compromise must win: it is
        // mid on price/duration and best-or-tied on every other metric.
        var extremeFast = Make("extreme-fast", price: 2000, durationMin: 1400, transfers: 4,
            risk: 0.6, waiting: 300, overnight: 1, comfort: 0.5, walking: 60);
        var extremeCheap = Make("extreme-cheap", price: 600, durationMin: 3000, transfers: 4,
            risk: 0.5, waiting: 700, overnight: 2, comfort: 0.4, walking: 240);
        var compromise = Make("compromise", price: 1100, durationMin: 1900, transfers: 1,
            risk: 0.1, waiting: 120, overnight: 0, comfort: 0.9, walking: 20);
        var winners = new ScoringService().PickPerProfile(new[] { extremeFast, extremeCheap, compromise });
        Assert.Equal("compromise", winners[OptimizationProfile.Balanced].Label);

        var breakdown = winners[OptimizationProfile.Balanced].Score!;
        Assert.Equal(8, breakdown.Components.Count);
        Assert.False(string.IsNullOrWhiteSpace(breakdown.Explanation));
        Assert.Equal(breakdown.Components.Sum(c => c.Weighted), breakdown.Total, 3);
    }

    [Fact]
    public void Explanation_Compares_Against_The_Fastest_Option()
    {
        var fast = Make("fast", price: 1500, durationMin: 1500, transfers: 3, overnight: 1);
        var calm = Make("calm", price: 900, durationMin: 1680, transfers: 1, overnight: 0);
        var winners = new ScoringService().PickPerProfile(new[] { fast, calm });
        var balanced = winners[OptimizationProfile.Balanced];
        Assert.Equal("calm", balanced.Label);
        Assert.Contains("longer than the fastest", balanced.Score!.Explanation);
        Assert.Contains("fewer transfer", balanced.Score.Explanation);
    }
}

public class WarningEngineTests
{
    private static WarningEngine Engine() => new(new MockWarningProvider());

    [Fact]
    public async Task Canonical_Route_Raises_Visa_Health_And_Passport_Notes_As_Demo_Data()
    {
        var candidates = await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest());
        var itinerary = candidates[0];
        await Engine().ApplyAsync(itinerary, "TR");
        Assert.Contains(itinerary.Warnings, w => w.Code == "visa_pe");
        Assert.Contains(itinerary.Warnings, w => w.Code == "health_pe");
        Assert.Contains(itinerary.Warnings, w => w.Code == "passport_validity");
        Assert.All(itinerary.Warnings, w => Assert.True(w.IsDemoData));
    }

    [Fact]
    public async Task Short_Connection_Is_Flagged()
    {
        var itinerary = SyntheticWith(new Transfer(MockWorld.Lim,
            new DateTime(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 19, 3, 40, 0, DateTimeKind.Utc),
            IsSelfTransfer: false, RequiresBaggageRecheck: false, IsOvernight: false));
        await Engine().ApplyAsync(itinerary, "TR");
        Assert.Contains(itinerary.Warnings, w => w.Code == "short_connection");
    }

    [Fact]
    public async Task Overnight_Airport_Wait_Is_Detected()
    {
        var itinerary = SyntheticWith(new Transfer(MockWorld.Lim,
            new DateTime(2026, 9, 19, 2, 0, 0, DateTimeKind.Utc),    // 21:00 Lima local
            new DateTime(2026, 9, 19, 10, 0, 0, DateTimeKind.Utc),   // 05:00 Lima local
            false, false, IsOvernight: true));
        await Engine().ApplyAsync(itinerary, "TR");
        Assert.Contains(itinerary.Warnings, w => w.Code == "overnight_airport");
    }

    [Fact]
    public async Task Self_Transfer_And_Airport_Change_Are_Flagged()
    {
        var istArrive = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);
        var segments = new[]
        {
            Flight(MockWorld.Mad, MockWorld.Ist, istArrive.AddHours(-4), istArrive),
            Flight(MockWorld.Saw, MockWorld.Mad, istArrive.AddHours(4), istArrive.AddHours(8), separate: true),
            Activity(),
        };
        var itinerary = Synthetic(segments,
            new Transfer(MockWorld.Ist, istArrive, istArrive.AddHours(4), IsSelfTransfer: true, true, false));
        await Engine().ApplyAsync(itinerary, "TR");
        Assert.Contains(itinerary.Warnings, w => w.Code == "airport_change");   // IST → SAW
        Assert.Contains(itinerary.Warnings, w => w.Code == "self_transfer");
        Assert.Contains(itinerary.Warnings, w => w.Code == "baggage_recheck");
    }

    [Fact]
    public async Task Long_Walk_And_Seasonal_Closure_Are_Flagged()
    {
        var walkStart = new DateTime(2027, 2, 10, 14, 0, 0, DateTimeKind.Utc);
        var segments = new[]
        {
            new ItinerarySegment
            {
                Mode = SegmentMode.Walking, From = MockWorld.Hidro, To = MockWorld.Aguas,
                DepartUtc = walkStart, ArriveUtc = walkStart.AddMinutes(165),
            },
            Activity(new DateTime(2027, 2, 11, 12, 0, 0, DateTimeKind.Utc)),   // February!
        };
        var itinerary = Synthetic(segments);
        await Engine().ApplyAsync(itinerary, "TR");
        Assert.Contains(itinerary.Warnings, w => w.Code == "long_walk");
        Assert.Contains(itinerary.Warnings, w => w.Code == "seasonal_closure");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────
    private static ItinerarySegment Flight(TransportNode from, TransportNode to,
        DateTime dep, DateTime arr, bool separate = false) => new()
    {
        Mode = from.Location.CountryCode == to.Location.CountryCode
            ? SegmentMode.DomesticFlight : SegmentMode.InternationalFlight,
        From = from, To = to, DepartUtc = dep, ArriveUtc = arr, IsSeparateTicket = separate,
    };

    private static ItinerarySegment Activity(DateTime? at = null)
    {
        var t = at ?? new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        return new ItinerarySegment
        {
            Mode = SegmentMode.Activity, From = MockWorld.Machu, To = MockWorld.Machu,
            DepartUtc = t, ArriveUtc = t.AddHours(3), Note = "Machu Picchu citadel entry (demo ticket)",
        };
    }

    private static Itinerary SyntheticWith(Transfer transfer) =>
        Synthetic(new[] { Activity() }, transfer);

    private static Itinerary Synthetic(IReadOnlyList<ItinerarySegment> segments, params Transfer[] transfers) => new()
    {
        Label = "synthetic", Segments = segments, Transfers = transfers,
        TotalPrice = new Money(1, "USD"), TotalDurationMinutes = 1000, TransferCount = transfers.Length,
        WalkingMinutes = 0, WaitingMinutes = 0, OvernightWaits = 0,
        RiskScore = 0, ComfortScore = 1, CarbonKgEstimate = 0,
        RequiredDocuments = Array.Empty<string>(),
    };
}
