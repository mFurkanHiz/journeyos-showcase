using JourneyOS.Showcase.Application;
using JourneyOS.Showcase.Domain;
using JourneyOS.Showcase.Infrastructure;

namespace JourneyOS.Showcase.Tests;

public static class TestWorld
{
    public static readonly DateOnly DemoDate = new(2026, 9, 18);

    public static TripSearchRequest CanonicalRequest(string currency = "USD") => new()
    {
        Origin = "Beykoz, Istanbul",
        Destination = "Machu Picchu",
        DepartureDate = DemoDate,
        Travellers = 1,
        Currency = currency,
    };

    public static ItineraryComposer Composer(
        IAirportAccessProvider? access = null,
        IFlightSearchProvider? flights = null) => new(
        new MockPlaceResolver(), new MockGatewayDiscovery(),
        access ?? new MockAirportAccessProvider(),
        new MockUrbanTransportProvider(), new MockPublicTransportProvider(),
        flights ?? new MockFlightProvider(),
        new MockHotelProvider(), new MockActivityProvider(), new DemoCurrencyConverter());
}

public class ComposerTests
{
    [Fact]
    public async Task Canonical_Route_Produces_Multiple_Distinct_Candidates()
    {
        var candidates = (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        Assert.True(candidates.Count >= 3, $"expected ≥3 candidates, got {candidates.Count}");
        // Real variety, not clones: several distinct totals and both approach flavours.
        Assert.True(candidates.Select(c => c.TotalPrice.Amount).Distinct().Count() >= 2);
        Assert.Contains(candidates, c => c.Label.Contains("trek"));
        Assert.Contains(candidates, c => c.Label.Contains("rail"));
    }

    [Fact]
    public async Task Segments_Are_Continuous_In_Space_And_Time()
    {
        foreach (var itinerary in (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates)
        {
            var segs = itinerary.Segments;
            for (var i = 0; i < segs.Count; i++)
            {
                Assert.True(segs[i].ArriveUtc >= segs[i].DepartUtc, "segment ends before it starts");
                if (i == 0) continue;
                Assert.True(segs[i].DepartUtc >= segs[i - 1].ArriveUtc,
                    $"time gap/overlap between {segs[i - 1].Mode} and {segs[i].Mode}");
                Assert.Equal(segs[i - 1].To.Code, segs[i].From.Code);   // space continuity
            }
        }
    }

    [Fact]
    public async Task No_Duplicate_Segments()
    {
        foreach (var itinerary in (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates)
        {
            var moves = itinerary.Segments
                .Where(s => s.From.Code != s.To.Code)
                .Select(s => $"{s.Mode}:{s.From.Code}>{s.To.Code}:{s.DepartUtc:O}")
                .ToList();
            Assert.Equal(moves.Count, moves.Distinct().Count());
        }
    }

    [Fact]
    public async Task Canonical_Route_Covers_The_Door_To_Door_Segment_Spectrum()
    {
        var candidates = (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        var allModes = candidates.SelectMany(c => c.Segments).Select(s => s.Mode).ToHashSet();
        foreach (var expected in new[]
        {
            SegmentMode.InternationalFlight, SegmentMode.DomesticFlight, SegmentMode.Train,
            SegmentMode.IntercityBus, SegmentMode.Walking, SegmentMode.UrbanTransit,
            SegmentMode.HotelStay, SegmentMode.Wait, SegmentMode.Checkpoint, SegmentMode.Activity,
        })
            Assert.Contains(expected, allModes);
        // Access happens by taxi or shuttle depending on the candidate.
        Assert.True(allModes.Contains(SegmentMode.Taxi) || allModes.Contains(SegmentMode.AirportAccess));
    }

    [Fact]
    public async Task Same_Request_Twice_Is_Deterministic()
    {
        var a = (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        var b = (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        string Fingerprint(IReadOnlyList<Itinerary> list) => string.Join(";",
            list.OrderBy(i => i.Label).Select(i => $"{i.Label}|{i.TotalPrice.Amount}|{i.TotalDurationMinutes}"));
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public async Task A_Failing_Provider_Removes_Its_Candidates_Not_The_Search()
    {
        var flaky = new FlakyAccessProvider();   // throws for SAW, works for IST
        var candidates = (await TestWorld.Composer(access: flaky).ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.DoesNotContain(c.Segments, s => s.To.Code == "SAW"));
    }

    private sealed class FlakyAccessProvider : IAirportAccessProvider
    {
        private readonly MockAirportAccessProvider _inner = new();
        public Task<IReadOnlyList<TransportOffer>> GetAccessAsync(
            TransportNode place, TransportNode airport, DateTime earliestDepartUtc, CancellationToken ct = default)
            => airport.Code == "SAW"
                ? throw new InvalidOperationException("provider down")
                : _inner.GetAccessAsync(place, airport, earliestDepartUtc, ct);
    }

    [Fact]
    public async Task Unknown_Route_Fails_With_A_Meaningful_Reason()
    {
        var ex = await Assert.ThrowsAsync<NoRouteFoundException>(() =>
            TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest() with { Destination = "Atlantis" }));
        Assert.StartsWith("unknown_destination", ex.Message);
    }

    [Fact]
    public async Task Preferences_Filter_The_Candidate_Set()
    {
        var all = (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        var capped = (await TestWorld.Composer().ComposeAsync(
            TestWorld.CanonicalRequest() with { MaxTransfers = all.Min(c => c.TransferCount) })).Candidates;
        Assert.True(capped.Max(c => c.TransferCount) <= all.Min(c => c.TransferCount));

        var noOvernight = (await TestWorld.Composer().ComposeAsync(
            TestWorld.CanonicalRequest() with { AvoidOvernightLayovers = true })).Candidates;
        // Either overnight-free candidates exist and all returned ones are clean,
        // or none exist and the preference degrades gracefully to the full set.
        if (all.Any(c => c.OvernightWaits == 0))
            Assert.All(noOvernight, c => Assert.Equal(0, c.OvernightWaits));
    }

    [Fact]
    public async Task An_Impossible_Preference_Is_Relaxed_AND_Reported()
    {
        // Nothing reaches Machu Picchu in a single transfer. The engine must still
        // return a trip — but it must not pretend the request was honoured.
        var result = await TestWorld.Composer().ComposeAsync(
            TestWorld.CanonicalRequest() with { MaxTransfers = 1 });

        Assert.NotEmpty(result.Candidates);
        Assert.All(result.Candidates, c => Assert.True(c.TransferCount > 1));

        var relaxed = Assert.Single(result.Relaxed);
        Assert.Equal("maxTransfers", relaxed.Preference);
        Assert.Contains("1 transfer or fewer", relaxed.Reason);   // not "1 transfers"
    }

    [Fact]
    public async Task A_Satisfiable_Search_Reports_Nothing_Relaxed()
    {
        var result = await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest());
        Assert.Empty(result.Relaxed);
    }
}

public class HaversineTests
{
    [Fact]
    public void Known_Distances_Are_Close()
    {
        // Istanbul Airport → Madrid Barajas ≈ 2,735 km great-circle.
        var d = Haversine.DistanceKm(41.262, 28.742, 40.472, -3.561);
        Assert.InRange(d, 2650, 2820);
        // Lima → Cusco ≈ 585 km.
        Assert.InRange(Haversine.DistanceKm(-12.022, -77.114, -13.536, -71.939), 560, 610);
        // Zero distance.
        Assert.Equal(0, Haversine.DistanceKm(41, 29, 41, 29), 3);
    }
}

public class NormalizationTests
{
    [Fact]
    public async Task Flight_Wire_Format_Is_Normalized_To_Canonical_Utc_Offers()
    {
        var offers = await new MockFlightProvider().SearchAsync(MockWorld.Ist, MockWorld.Mad, TestWorld.DemoDate);
        Assert.NotEmpty(offers);
        foreach (var o in offers)
        {
            Assert.Equal(SegmentMode.InternationalFlight, o.Mode);   // TR → ES
            Assert.Equal("USD", o.Price.Currency);
            Assert.True(o.Price.Amount > 0);
            Assert.True(o.ArriveUtc > o.DepartUtc);
            // 07:55 Istanbul local = 04:55 UTC — the adapter must convert local→UTC.
            var first = offers.MinBy(x => x.DepartUtc)!;
            Assert.Equal(4, first.DepartUtc.Hour);
            Assert.Equal(55, first.DepartUtc.Minute);
        }
    }

    [Fact]
    public async Task Ground_Wire_Format_Is_Normalized_With_Modes_Preserved()
    {
        var offers = await new MockPublicTransportProvider().DeparturesAsync(
            MockWorld.Ollanta, MockWorld.LocalToUtc(MockWorld.Ollanta, TestWorld.DemoDate, new TimeOnly(6, 0)));
        Assert.NotEmpty(offers);
        Assert.All(offers, o => Assert.Equal(SegmentMode.Train, o.Mode));
        Assert.All(offers, o => Assert.Equal("MockPublicTransportProvider", o.ProviderName));
    }
}

public class TimeZoneTests
{
    [Fact]
    public async Task Istanbul_And_Lima_Clocks_Render_With_Their_Own_Offsets()
    {
        var candidates = (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        var timeline = new TimelineBuilder().Build(candidates[0]);
        Assert.Contains(timeline.Entries, e => e.StartLocal.Contains("UTC+03:00"));
        Assert.Contains(timeline.Entries, e => e.EndLocal.Contains("UTC-05:00"));
        Assert.Contains(timeline.Entries, e => e.CrossesTimeZone);
    }

    [Fact]
    public async Task Date_Rollover_And_Arrival_Day_Offset_Are_Reported()
    {
        var candidates = (await TestWorld.Composer().ComposeAsync(TestWorld.CanonicalRequest())).Candidates;
        foreach (var itinerary in candidates)
        {
            var timeline = new TimelineBuilder().Build(itinerary);
            // Istanbul→Peru door-to-gate spans at least one calendar day.
            Assert.True(timeline.ArrivalDayOffset >= 1,
                $"expected next-day+ arrival, got {timeline.ArrivalDayOffset}");
            Assert.Contains(timeline.Entries, e => e.DateChanges);
        }
    }
}
