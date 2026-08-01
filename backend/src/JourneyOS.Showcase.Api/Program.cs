using JourneyOS.Showcase.Api;
using JourneyOS.Showcase.Application;
using JourneyOS.Showcase.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Providers — swap any mock for a live adapter here and NOTHING else changes.
builder.Services.AddSingleton<IPlaceResolver, MockPlaceResolver>();
builder.Services.AddSingleton<IGeocodingProvider, MockGeocodingProvider>();
builder.Services.AddSingleton<IGatewayDiscoveryService, MockGatewayDiscovery>();
builder.Services.AddSingleton<IAirportAccessProvider, MockAirportAccessProvider>();
builder.Services.AddSingleton<IUrbanRoutingProvider, MockUrbanTransportProvider>();
builder.Services.AddSingleton<IPublicTransportProvider, MockPublicTransportProvider>();
builder.Services.AddSingleton<IFlightSearchProvider, MockFlightProvider>();
builder.Services.AddSingleton<IHotelSearchProvider, MockHotelProvider>();
builder.Services.AddSingleton<IActivitySearchProvider, MockActivityProvider>();
builder.Services.AddSingleton<IRequirementWarningProvider, MockWarningProvider>();
builder.Services.AddSingleton<ICurrencyConverter, DemoCurrencyConverter>();
builder.Services.AddSingleton<ITripStore, InMemoryTripStore>();

// Composition core.
builder.Services.AddSingleton<ItineraryComposer>();
builder.Services.AddSingleton<ScoringService>();
builder.Services.AddSingleton<WarningEngine>();
builder.Services.AddSingleton<TimelineBuilder>();
builder.Services.AddSingleton<TripService>();

builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:5173", "http://localhost:4173")   // Vite dev/preview only
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.MapOpenApi();   // machine-readable spec at /openapi/v1.json

app.MapGet("/health", () => Results.Ok(new { status = "ok", demoData = true }));

app.MapPost("/api/trips/search", async (SearchRequestDto dto, TripService trips, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(dto.Origin) || string.IsNullOrWhiteSpace(dto.Destination))
        return Results.BadRequest(new { error = "origin_and_destination_required" });
    if (dto.Travellers is < 1 or > 9)
        return Results.BadRequest(new { error = "travellers_out_of_range" });
    if (dto.ReturnDate is { } r && r < dto.DepartureDate)
        return Results.BadRequest(new { error = "return_before_departure" });

    try
    {
        var trip = await trips.SearchAsync(new TripSearchRequest
        {
            Origin = dto.Origin,
            Destination = dto.Destination,
            DepartureDate = dto.DepartureDate,
            ReturnDate = dto.ReturnDate,
            Travellers = dto.Travellers,
            Currency = dto.Currency,
            MaxTransfers = dto.MaxTransfers,
            AvoidOvernightLayovers = dto.AvoidOvernightLayovers,
            ReducedWalking = dto.ReducedWalking,
        }, ct);
        return Results.Ok(trip.ToDto());
    }
    catch (NoRouteFoundException ex)
    {
        // A meaningful, machine-readable reason — never a bare 500.
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
});

app.MapGet("/api/trips/{tripId:guid}", (Guid tripId, ITripStore store) =>
    store.Get(tripId) is { } trip ? Results.Ok(trip.ToDto()) : Results.NotFound());

app.MapGet("/api/trips/{tripId:guid}/itineraries", (Guid tripId, ITripStore store) =>
    store.Get(tripId) is { } trip
        ? Results.Ok(trip.Itineraries.Select(i => i.ToSummaryDto()))
        : Results.NotFound());

app.MapGet("/api/trips/{tripId:guid}/itineraries/{itineraryId:guid}",
    (Guid tripId, Guid itineraryId, ITripStore store, TimelineBuilder timeline) =>
    {
        var trip = store.Get(tripId);
        var itinerary = trip?.Itineraries.FirstOrDefault(i => i.Id == itineraryId);
        return itinerary is null ? Results.NotFound() : Results.Ok(itinerary.ToDetailDto(timeline));
    });

app.MapGet("/api/trips/{tripId:guid}/comparison", (Guid tripId, ITripStore store) =>
    store.Get(tripId) is { } trip ? Results.Ok(trip.ToComparison()) : Results.NotFound());

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
