using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JourneyOS.Showcase.Tests;

/// <summary>Full-HTTP integration tests through WebApplicationFactory — the same
/// pipeline, DI graph and JSON contracts a real client sees.</summary>
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    public ApiIntegrationTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    private static object CanonicalSearch(string currency = "USD") => new
    {
        origin = "Beykoz, Istanbul",
        destination = "Machu Picchu",
        departureDate = "2026-09-18",
        travellers = 1,
        currency,
    };

    private async Task<JsonElement> SearchAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/trips/search", CanonicalSearch());
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Fact]
    public async Task Health_Reports_Ok_And_Admits_Demo_Data()
    {
        var resp = await _client.GetFromJsonAsync<JsonElement>("/health", Json);
        Assert.Equal("ok", resp.GetProperty("status").GetString());
        Assert.True(resp.GetProperty("demoData").GetBoolean());
    }

    [Fact]
    public async Task Search_Returns_Three_Profiles_Over_Real_Alternatives()
    {
        var trip = await SearchAsync();
        var profiles = trip.GetProperty("profiles");
        Assert.True(profiles.TryGetProperty("Fastest", out _));
        Assert.True(profiles.TryGetProperty("Cheapest", out _));
        Assert.True(profiles.TryGetProperty("Balanced", out _));
        Assert.True(trip.GetProperty("itineraries").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Trip_And_Itinerary_Endpoints_Round_Trip()
    {
        var trip = await SearchAsync();
        var tripId = trip.GetProperty("id").GetGuid();

        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/trips/{tripId}", Json);
        Assert.Equal(tripId, fetched.GetProperty("id").GetGuid());

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/trips/{tripId}/itineraries", Json);
        var firstId = list.EnumerateArray().First().GetProperty("id").GetGuid();

        var detail = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/trips/{tripId}/itineraries/{firstId}", Json);
        Assert.True(detail.GetProperty("segments").GetArrayLength() > 5);
        Assert.True(detail.GetProperty("timeline").GetProperty("entries").GetArrayLength() > 5);
        Assert.True(detail.GetProperty("warnings").GetArrayLength() > 0);
        Assert.True(detail.GetProperty("warnings").EnumerateArray()
            .All(w => w.GetProperty("isDemoData").GetBoolean()));
    }

    [Fact]
    public async Task Comparison_Endpoint_Builds_A_Side_By_Side_Table()
    {
        var trip = await SearchAsync();
        var tripId = trip.GetProperty("id").GetGuid();
        var rows = await _client.GetFromJsonAsync<JsonElement>($"/api/trips/{tripId}/comparison", Json);
        Assert.True(rows.GetArrayLength() >= 8);
        var priceRow = rows.EnumerateArray().First(r => r.GetProperty("metric").GetString() == "Total price");
        Assert.True(priceRow.GetProperty("byProfile").TryGetProperty("Balanced", out _));
    }

    [Fact]
    public async Task Comparison_Numbers_Use_The_Invariant_Culture_Not_The_Servers()
    {
        // The API contract is English; a tr-TR server must not ship "0,41" to every client.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
        try
        {
            var trip = await SearchAsync();
            var tripId = trip.GetProperty("id").GetGuid();
            var rows = await _client.GetFromJsonAsync<JsonElement>($"/api/trips/{tripId}/comparison", Json);

            var decimals = rows.EnumerateArray()
                .Where(r => r.GetProperty("metric").GetString()!.Contains("(0-1)"))
                .SelectMany(r => r.GetProperty("byProfile").EnumerateObject().Select(p => p.Value.GetString()!))
                .ToList();

            Assert.NotEmpty(decimals);
            Assert.All(decimals, v => Assert.DoesNotContain(",", v));
            Assert.All(decimals, v => Assert.Contains(".", v));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Bad_Input_Is_400_Unknown_Trip_Is_404_No_Route_Is_422()
    {
        var bad = await _client.PostAsJsonAsync("/api/trips/search",
            new { origin = "", destination = "Machu Picchu", departureDate = "2026-09-18" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var missing = await _client.GetAsync($"/api/trips/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var noRoute = await _client.PostAsJsonAsync("/api/trips/search",
            new { origin = "Beykoz", destination = "Atlantis", departureDate = "2026-09-18" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noRoute.StatusCode);
        var body = await noRoute.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.StartsWith("unknown_destination", body.GetProperty("error").GetString());
    }
}

/// <summary>The canonical Beykoz → Machu Picchu demo, end to end over HTTP: the
/// promise on the README is exactly what this test proves.</summary>
public class EndToEndCanonicalDemoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _client;
    public EndToEndCanonicalDemoTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Beykoz_To_Machu_Picchu_Delivers_Three_Honest_Profiles()
    {
        var resp = await _client.PostAsJsonAsync("/api/trips/search", new
        {
            origin = "Beykoz, Istanbul",
            destination = "Machu Picchu",
            departureDate = "2026-09-18",
            travellers = 2,
            currency = "EUR",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var trip = await resp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var tripId = trip.GetProperty("id").GetGuid();
        var profiles = trip.GetProperty("profiles");
        var itineraries = trip.GetProperty("itineraries").EnumerateArray()
            .ToDictionary(i => i.GetProperty("id").GetGuid());

        // Profile picks must honour their own definitions across the STORED winners.
        var fastest = itineraries[profiles.GetProperty("Fastest").GetGuid()];
        var cheapest = itineraries[profiles.GetProperty("Cheapest").GetGuid()];
        foreach (var i in itineraries.Values)
        {
            Assert.True(fastest.GetProperty("totalDurationMinutes").GetInt32()
                        <= i.GetProperty("totalDurationMinutes").GetInt32());
            Assert.True(cheapest.GetProperty("totalPrice").GetDecimal()
                        <= i.GetProperty("totalPrice").GetDecimal());
        }
        Assert.All(itineraries.Values, i =>
            Assert.Equal("EUR", i.GetProperty("currency").GetString()));

        // Every winner is a real door-to-door chain with a continuous timeline. Each
        // spans a night away (a hotel stay OR an overnight airport/gate wait — the
        // "hotel flavour" is one candidate axis, not a guarantee on every winner).
        var anyHotel = false;
        foreach (var id in itineraries.Keys)
        {
            var detail = await _client.GetFromJsonAsync<JsonElement>(
                $"/api/trips/{tripId}/itineraries/{id}", Json);
            var modes = detail.GetProperty("segments").EnumerateArray()
                .Select(s => s.GetProperty("mode").GetString()).ToHashSet();
            Assert.Contains("InternationalFlight", modes);
            Assert.Contains("DomesticFlight", modes);
            Assert.Contains("Activity", modes);
            Assert.True(modes.Contains("Train") || modes.Contains("IntercityBus"));
            Assert.True(modes.Contains("HotelStay") || modes.Contains("Wait"),
                "a 2+ day journey must rest somewhere (hotel or overnight wait)");
            anyHotel |= modes.Contains("HotelStay");

            var timeline = detail.GetProperty("timeline");
            Assert.True(timeline.GetProperty("arrivalDayOffset").GetInt32() >= 1);
            var entries = timeline.GetProperty("entries").EnumerateArray().ToList();
            for (var k = 1; k < entries.Count; k++)
                Assert.True(entries[k].GetProperty("startUtc").GetDateTime()
                            >= entries[k - 1].GetProperty("startUtc").GetDateTime());

            Assert.True(detail.GetProperty("warnings").GetArrayLength() > 0);
            Assert.True(detail.GetProperty("summary").GetProperty("score").ValueKind != JsonValueKind.Null);
        }
        Assert.True(anyHotel, "at least one profile winner should include a hotel night");
    }
}
