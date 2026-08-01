using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Application;

public sealed record TimelineEntry(
    string Kind,                // segment | checkpoint | wait | hotel | activity | note
    string Title,
    SegmentMode? Mode,
    DateTime StartUtc,
    DateTime EndUtc,
    string StartLocal,          // "09:40 (UTC+03:00, Istanbul)"
    string EndLocal,
    int DurationMinutes,
    bool CrossesTimeZone,
    bool DateChanges,
    string? Note);

public sealed record Timeline(
    IReadOnlyList<TimelineEntry> Entries,
    int ArrivalDayOffset,       // destination local date minus departure local date
    string DepartureLocal,
    string ArrivalLocal);

/// <summary>Renders a composed itinerary as a human timeline. Every entry carries
/// UTC plus BOTH endpoint local clocks, so a Lima arrival never silently shows an
/// Istanbul time; timezone changes and date rollovers are called out explicitly
/// (docs/timeline.md).</summary>
public sealed class TimelineBuilder
{
    public Timeline Build(Itinerary itinerary)
    {
        var entries = new List<TimelineEntry>();
        foreach (var s in itinerary.Segments)
        {
            var kind = s.Mode switch
            {
                SegmentMode.Checkpoint => "checkpoint",
                SegmentMode.Wait => "wait",
                SegmentMode.HotelStay => "hotel",
                SegmentMode.Activity => "activity",
                _ => "segment",
            };
            var startLoc = s.From.Location;
            var endLoc = s.To.Location;
            var startLocal = startLoc.ToLocal(s.DepartUtc);
            var endLocal = endLoc.ToLocal(s.ArriveUtc);
            entries.Add(new TimelineEntry(
                kind,
                Title(s),
                s.Mode,
                s.DepartUtc,
                s.ArriveUtc,
                $"{startLocal:HH:mm} ({startLoc.UtcOffsetLabel}, {startLoc.Name})",
                $"{endLocal:HH:mm} ({endLoc.UtcOffsetLabel}, {endLoc.Name})",
                s.DurationMinutes,
                CrossesTimeZone: startLoc.UtcOffsetMinutes != endLoc.UtcOffsetMinutes,
                DateChanges: startLocal.Date != endLocal.Date,
                s.Note));
        }

        var firstLoc = itinerary.Segments[0].From.Location;
        var lastArrival = itinerary.Segments.Last(s => s.Mode == SegmentMode.Activity);
        var lastLoc = lastArrival.To.Location;
        var departLocal = firstLoc.ToLocal(itinerary.DepartUtc);
        var arriveLocal = lastLoc.ToLocal(lastArrival.DepartUtc);   // arrival at the gate
        return new Timeline(
            entries,
            ArrivalDayOffset: (arriveLocal.Date - departLocal.Date).Days,
            DepartureLocal: $"{departLocal:yyyy-MM-dd HH:mm} ({firstLoc.UtcOffsetLabel}, {firstLoc.Name})",
            ArrivalLocal: $"{arriveLocal:yyyy-MM-dd HH:mm} ({lastLoc.UtcOffsetLabel}, {lastLoc.Name})");
    }

    private static string Title(ItinerarySegment s) => s.Mode switch
    {
        SegmentMode.Checkpoint => s.Note ?? "Checkpoint",
        SegmentMode.Wait => s.Note ?? "Wait",
        SegmentMode.HotelStay => $"Hotel night — {s.Note}",
        SegmentMode.Activity => s.Note ?? "Activity",
        _ => $"{s.From.Name} → {s.To.Name}" + (s.Carrier is { } c ? $" ({c})" : ""),
    };
}
