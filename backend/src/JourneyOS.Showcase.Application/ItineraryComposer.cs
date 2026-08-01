using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Application;

public sealed class NoRouteFoundException(string reason) : Exception(reason);

/// <summary>Builds full door-to-door candidate itineraries by walking the provider
/// seams: place → airport access → flight path (≤4 legs, searched over the flight
/// network) → destination ground approach → overnight stays → the destination
/// activity. The composer deliberately enumerates a CANDIDATE SPACE (origin gateway
/// × flight path × ground approach × access mode × overnight handling) rather than
/// one "best" answer — scoring picks per-profile winners from real alternatives.
/// A provider that throws removes its candidates, never the whole search.</summary>
public sealed class ItineraryComposer
{
    private const int IntlCheckInMinutes = 120;
    private const int DomCheckInMinutes = 75;
    private const int PassportControlMinutes = 30;
    private const int BaggageRecheckMinutes = 45;
    private const int MinGroundConnectMinutes = 15;
    private const int OvernightHotelThresholdMinutes = 6 * 60;
    private const int MaxCandidates = 40;
    private const int GateFirstEntryHour = 6;    // local citadel opening
    private const int GateLastEntryHour = 14;    // local last entry

    private readonly IPlaceResolver _places;
    private readonly IGatewayDiscoveryService _gateways;
    private readonly IAirportAccessProvider _access;
    private readonly IUrbanRoutingProvider _urban;
    private readonly IPublicTransportProvider _ground;
    private readonly IFlightSearchProvider _flights;
    private readonly IHotelSearchProvider _hotels;
    private readonly IActivitySearchProvider _activities;
    private readonly ICurrencyConverter _fx;

    public ItineraryComposer(
        IPlaceResolver places, IGatewayDiscoveryService gateways, IAirportAccessProvider access,
        IUrbanRoutingProvider urban, IPublicTransportProvider ground, IFlightSearchProvider flights,
        IHotelSearchProvider hotels, IActivitySearchProvider activities, ICurrencyConverter fx)
    {
        _places = places; _gateways = gateways; _access = access; _urban = urban;
        _ground = ground; _flights = flights; _hotels = hotels; _activities = activities; _fx = fx;
    }

    public async Task<IReadOnlyList<Itinerary>> ComposeAsync(TripSearchRequest req, CancellationToken ct = default)
    {
        if (!_fx.Supports(req.Currency)) throw new NoRouteFoundException($"unsupported_currency:{req.Currency}");

        var origin = await _places.ResolveAsync(req.Origin, ct)
            ?? throw new NoRouteFoundException($"unknown_origin:{req.Origin}");
        var destination = await _places.ResolveAsync(req.Destination, ct)
            ?? throw new NoRouteFoundException($"unknown_destination:{req.Destination}");

        var originGateways = await _gateways.FindGatewaysAsync(origin.Location, 2, ct);
        var destGateways = await _gateways.FindGatewaysAsync(destination.Location, 1, ct);
        if (originGateways.Count == 0 || destGateways.Count == 0)
            throw new NoRouteFoundException("no_gateway_airport");
        var destGateway = destGateways[0];

        var candidates = new List<Itinerary>();
        foreach (var og in originGateways)
        {
            var paths = await FindFlightPathsAsync(og, destGateway, ct);
            var groundApproaches = await FindGroundApproachesAsync(destGateway, destination, ct);
            IReadOnlyList<TransportOffer> accessOptions;
            try
            {
                accessOptions = await _access.GetAccessAsync(
                    origin, og, DepartureCursorUtc(req.DepartureDate, origin.Location), ct);
            }
            catch { continue; }   // fail-soft: this gateway's access provider is down

            foreach (var path in paths)
                foreach (var approach in groundApproaches)
                    foreach (var access in accessOptions)
                        foreach (var hotelOvernights in new[] { true, false })
                        {
                            if (candidates.Count >= MaxCandidates) break;
                            try
                            {
                                var built = await BuildCandidateAsync(
                                    req, origin, destination, access, path, approach, hotelOvernights, ct);
                                if (built is not null) candidates.Add(built);
                            }
                            catch (NoRouteFoundException) { /* this combination has no schedule — skip */ }
                            catch { /* a provider failed mid-build — skip the candidate, keep searching */ }
                        }
        }

        candidates = Dedupe(candidates);
        candidates = ApplyHardPreferences(req, candidates);
        if (candidates.Count == 0)
            throw new NoRouteFoundException($"no_route:{req.Origin}->{req.Destination}");
        return candidates;
    }

    /// <summary>06:00 local at the origin on the requested date — a civilised start.</summary>
    private static DateTime DepartureCursorUtc(DateOnly date, Location origin) =>
        date.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc).AddMinutes(-origin.UtcOffsetMinutes);

    /// <summary>BFS over the flight network, ≤4 legs, no revisits.</summary>
    private async Task<List<List<TransportNode>>> FindFlightPathsAsync(
        TransportNode from, TransportNode to, CancellationToken ct)
    {
        var results = new List<List<TransportNode>>();
        var queue = new Queue<List<TransportNode>>();
        queue.Enqueue(new List<TransportNode> { from });
        while (queue.Count > 0 && results.Count < 8)
        {
            var path = queue.Dequeue();
            var last = path[^1];
            if (last.Code == to.Code) { results.Add(path); continue; }
            if (path.Count >= 5) continue;   // 4 legs max
            IReadOnlyList<TransportNode> next;
            try { next = await _flights.DestinationsFromAsync(last, ct); }
            catch { continue; }
            foreach (var n in next)
            {
                if (path.Any(p => p.Code == n.Code)) continue;
                // Prune: never fly a leg that moves AWAY from the target by more than it helps.
                var before = Haversine.DistanceKm(last.Location, to.Location);
                var after = Haversine.DistanceKm(n.Location, to.Location);
                if (after > before + 500) continue;
                queue.Enqueue(new List<TransportNode>(path) { n });
            }
        }
        return results;
    }

    /// <summary>DFS over scheduled ground legs from the arrival airport to the final
    /// destination node — the Peru chain(s): rail via Ollantaytambo, or the budget
    /// bus + hydroelectric trek. Returns node paths; times are resolved at build.</summary>
    private async Task<List<List<TransportNode>>> FindGroundApproachesAsync(
        TransportNode airport, TransportNode destination, CancellationToken ct)
    {
        var results = new List<List<TransportNode>>();
        async Task WalkAsync(List<TransportNode> path)
        {
            if (results.Count >= 6) return;
            var last = path[^1];
            if (last.Code == destination.Code) { results.Add(path); return; }
            if (path.Count >= 7) return;
            IReadOnlyList<TransportOffer> legs;
            try { legs = await _ground.DeparturesAsync(last, DateTime.MinValue, ct); }
            catch { return; }
            foreach (var next in legs.Select(l => l.To).DistinctBy(n => n.Code))
            {
                if (path.Any(p => p.Code == next.Code)) continue;
                await WalkAsync(new List<TransportNode>(path) { next });
            }
        }

        await WalkAsync(new List<TransportNode> { airport });
        return results.Count > 0 ? results : throw new NoRouteFoundException("no_ground_approach");
    }

    private async Task<Itinerary?> BuildCandidateAsync(
        TripSearchRequest req, TransportNode origin, TransportNode destination,
        TransportOffer accessOffer, List<TransportNode> flightPath, List<TransportNode> approach,
        bool hotelOvernights, CancellationToken ct)
    {
        var segments = new List<ItinerarySegment>();
        var transfers = new List<Transfer>();
        var cursor = DepartureCursorUtc(req.DepartureDate, origin.Location);

        // 1. Door → airport.
        var accessDepart = cursor > accessOffer.DepartUtc ? cursor : accessOffer.DepartUtc;
        var accessArrive = accessDepart.AddMinutes(accessOffer.DurationMinutes);
        segments.Add(new ItinerarySegment
        {
            Mode = accessOffer.Mode, From = origin, To = accessOffer.To,
            DepartUtc = accessDepart, ArriveUtc = accessArrive,
            Price = accessOffer.Price, Carrier = accessOffer.Carrier,
        });
        cursor = accessArrive;

        // 2. Flight chain with check-in/security, passport control, connection waits.
        for (var i = 0; i < flightPath.Count - 1; i++)
        {
            var from = flightPath[i];
            var to = flightPath[i + 1];
            var intl = from.Location.CountryCode != to.Location.CountryCode;
            var checkIn = i == 0 ? (intl ? IntlCheckInMinutes : DomCheckInMinutes)
                                 : (intl ? 60 : 45);          // connections need less
            var earliestBoardable = cursor.AddMinutes(checkIn);

            var offer = await FirstFlightOnOrAfterAsync(from, to, earliestBoardable, ct)
                ?? throw new NoRouteFoundException($"no_flight:{from.Code}->{to.Code}");

            // Check-in / security / passport queue occupies the tail of the wait.
            var gap = (int)(offer.DepartUtc - cursor).TotalMinutes;
            if (gap > checkIn)
                await AddWaitOrHotel(segments, transfers, from, cursor, offer.DepartUtc.AddMinutes(-checkIn),
                    hotelOvernights, req, ct);
            segments.Add(new ItinerarySegment
            {
                Mode = SegmentMode.Checkpoint, From = from, To = from,
                DepartUtc = offer.DepartUtc.AddMinutes(-checkIn), ArriveUtc = offer.DepartUtc,
                Note = i == 0 ? "Check-in, security" : intl ? "Security, passport control" : "Security",
            });
            segments.Add(new ItinerarySegment
            {
                Mode = intl ? SegmentMode.InternationalFlight : SegmentMode.DomesticFlight,
                From = from, To = to, DepartUtc = offer.DepartUtc, ArriveUtc = offer.ArriveUtc,
                Price = offer.Price, Carrier = offer.Carrier, IsSeparateTicket = offer.IsSeparateTicket,
            });
            transfers.Add(new Transfer(from, cursor, offer.DepartUtc,
                offer.IsSeparateTicket, offer.IsSeparateTicket, IsOvernightLocal(from.Location, cursor, offer.DepartUtc)));
            cursor = offer.ArriveUtc;

            if (intl)
            {
                segments.Add(new ItinerarySegment
                {
                    Mode = SegmentMode.Checkpoint, From = to, To = to,
                    DepartUtc = cursor, ArriveUtc = cursor.AddMinutes(PassportControlMinutes),
                    Note = "Passport control",
                });
                cursor = cursor.AddMinutes(PassportControlMinutes);
            }
            if (offer.IsSeparateTicket && i < flightPath.Count - 2)
            {
                segments.Add(new ItinerarySegment
                {
                    Mode = SegmentMode.Checkpoint, From = to, To = to,
                    DepartUtc = cursor, ArriveUtc = cursor.AddMinutes(BaggageRecheckMinutes),
                    Note = "Collect and re-check baggage (separate ticket)",
                });
                cursor = cursor.AddMinutes(BaggageRecheckMinutes);
            }
        }

        // 3. Ground approach up to the GATE TOWN (the node just before the final
        // destination) — the night before the visit is spent there, not at the gate.
        var preferCheapGround = !hotelOvernights;   // budget flavour rides the colectivo
        for (var i = 0; i < approach.Count - 2; i++)
        {
            var from = approach[i];
            var to = approach[i + 1];
            var offer = await FirstGroundOnOrAfterAsync(from, to, cursor.AddMinutes(MinGroundConnectMinutes), preferCheapGround, ct)
                ?? throw new NoRouteFoundException($"no_ground:{from.Code}->{to.Code}");
            if (offer.DepartUtc > cursor.AddMinutes(MinGroundConnectMinutes))
                await AddWaitOrHotel(segments, transfers, from, cursor, offer.DepartUtc, hotelOvernights, req, ct);
            transfers.Add(new Transfer(from, cursor, offer.DepartUtc, false, false,
                IsOvernightLocal(from.Location, cursor, offer.DepartUtc)));
            segments.Add(new ItinerarySegment
            {
                Mode = offer.Mode, From = from, To = to,
                DepartUtc = offer.DepartUtc, ArriveUtc = offer.ArriveUtc,
                Price = offer.Price, Carrier = offer.Carrier,
            });
            cursor = offer.ArriveUtc;
        }

        // 4. Ride the last hop up to the gate as soon as transport allows, then enter
        // at the FIRST feasible slot within opening hours — a faster journey arrives
        // earlier and enters earlier, so total door-to-door time genuinely differs by
        // route (it does NOT collapse to a single fixed morning). A late arrival waits
        // overnight at the gate town for the next opening.
        var gateTown = approach[^2];
        var finalLeg = await FirstGroundOnOrAfterAsync(
            gateTown, destination, cursor.AddMinutes(MinGroundConnectMinutes), preferCheapGround, ct)
            ?? throw new NoRouteFoundException($"no_ground:{gateTown.Code}->{destination.Code}");
        // If the gate is already closed for today, spend the night at the gate town and
        // ride up in the morning instead of arriving to a shut gate.
        if (LocalHour(destination, finalLeg.ArriveUtc) >= GateLastEntryHour)
        {
            var nextMorningUp = await FirstGroundOnOrAfterAsync(
                gateTown, destination, NextLocalOpen(destination, finalLeg.ArriveUtc).AddMinutes(-120),
                preferCheapGround, ct);
            if (nextMorningUp is not null) finalLeg = nextMorningUp;
        }
        if (finalLeg.DepartUtc > cursor.AddMinutes(MinGroundConnectMinutes))
            await AddWaitOrHotel(segments, transfers, gateTown, cursor, finalLeg.DepartUtc, hotelOvernights: true, req, ct);
        segments.Add(new ItinerarySegment
        {
            Mode = finalLeg.Mode, From = gateTown, To = destination,
            DepartUtc = finalLeg.DepartUtc, ArriveUtc = finalLeg.ArriveUtc,
            Price = finalLeg.Price, Carrier = finalLeg.Carrier,
        });
        cursor = finalLeg.ArriveUtc;

        var acts = await _activities.SearchAsync(
            destination, DateOnly.FromDateTime(destination.Location.ToLocal(cursor)), ct);
        var act = acts.FirstOrDefault(a => a.At.Code == destination.Code)
            ?? throw new NoRouteFoundException("no_destination_activity");
        var entryUtc = FeasibleEntryUtc(destination, cursor.AddMinutes(15));
        if (entryUtc > cursor.AddMinutes(15))
            await AddWaitOrHotel(segments, transfers, destination, cursor, entryUtc,
                hotelOvernights: IsOvernightLocal(destination.Location, cursor, entryUtc), req, ct);
        segments.Add(new ItinerarySegment
        {
            Mode = SegmentMode.Activity, From = destination, To = destination,
            DepartUtc = entryUtc, ArriveUtc = entryUtc.AddMinutes(act.DurationMinutes),
            Price = act.Price, Carrier = act.ProviderName, Note = act.Title,
        });

        return Finalize(req, segments, transfers, flightPath, approach);
    }

    private async Task<TransportOffer?> FirstFlightOnOrAfterAsync(
        TransportNode from, TransportNode to, DateTime earliestUtc, CancellationToken ct)
    {
        // Look on the local departure date, then the next two — overnight rolls happen.
        for (var d = 0; d < 3; d++)
        {
            var date = DateOnly.FromDateTime(from.Location.ToLocal(earliestUtc)).AddDays(d);
            var offers = await _flights.SearchAsync(from, to, date, ct);
            var hit = offers.Where(o => o.DepartUtc >= earliestUtc).OrderBy(o => o.DepartUtc).FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static int LocalHour(TransportNode node, DateTime utc) => node.Location.ToLocal(utc).Hour;

    /// <summary>The next local <see cref="GateFirstEntryHour"/> at or after the given
    /// instant (UTC), used to time a morning ride up to a closed gate.</summary>
    private static DateTime NextLocalOpen(TransportNode node, DateTime utc)
    {
        var local = node.Location.ToLocal(utc);
        var open = local.Date.AddHours(GateFirstEntryHour);
        if (local.Hour >= GateFirstEntryHour) open = open.AddDays(1);
        return DateTime.SpecifyKind(open, DateTimeKind.Utc).AddMinutes(-node.Location.UtcOffsetMinutes);
    }

    /// <summary>Earliest entry the traveller can actually take: as soon as they are at
    /// the gate within opening hours, else the next morning opening. Faster arrivals
    /// enter sooner — this is what makes duration a real differentiator.</summary>
    private static DateTime FeasibleEntryUtc(TransportNode gate, DateTime readyUtc)
    {
        var local = gate.Location.ToLocal(readyUtc);
        DateTime entryLocal;
        if (local.Hour < GateFirstEntryHour) entryLocal = local.Date.AddHours(GateFirstEntryHour);
        else if (local.Hour >= GateLastEntryHour) entryLocal = local.Date.AddDays(1).AddHours(GateFirstEntryHour);
        else entryLocal = local;
        return DateTime.SpecifyKind(entryLocal, DateTimeKind.Utc).AddMinutes(-gate.Location.UtcOffsetMinutes);
    }

    private async Task<TransportOffer?> FirstGroundOnOrAfterAsync(
        TransportNode from, TransportNode to, DateTime earliestUtc, bool preferCheap, CancellationToken ct)
    {
        var urban = await _urban.RouteAsync(from, to, earliestUtc, ct);
        var scheduled = await _ground.DeparturesAsync(from, earliestUtc, ct);
        var options = urban.Concat(scheduled)
            .Where(o => o.To.Code == to.Code && o.DepartUtc >= earliestUtc)
            .OrderBy(o => o.DepartUtc)
            .ToList();
        if (options.Count == 0) return null;
        var first = options[0];
        // Nobody hikes at dawn because the trail "departs" 60 min before the shuttle:
        // when a vehicle leaves within 90 min of the earliest walking option, ride it.
        if (first.Mode == SegmentMode.Walking)
        {
            var ride = options.FirstOrDefault(o =>
                o.Mode != SegmentMode.Walking &&
                (o.DepartUtc - first.DepartUtc).TotalMinutes <= 90);
            if (ride is not null) first = ride;
        }
        // Budget flavour: among departures within 45 min of the pick, take the
        // cheapest (the colectivo over the taxi); comfort flavour keeps the earliest.
        if (preferCheap)
        {
            var window = options.Where(o =>
                o.Mode != SegmentMode.Walking &&
                (o.DepartUtc - first.DepartUtc).TotalMinutes is >= 0 and <= 45).ToList();
            if (window.Count > 0) return window.OrderBy(o => o.Price.Amount).ThenBy(o => o.DepartUtc).First();
        }
        return first;
    }

    /// <summary>A long pause becomes an airport/station Wait, or a HotelStay when the
    /// candidate prefers beds and the pause is an overnight of 6h+ near a hotel node.</summary>
    private async Task AddWaitOrHotel(
        List<ItinerarySegment> segments, List<Transfer> transfers, TransportNode at,
        DateTime fromUtc, DateTime toUtc, bool hotelOvernights, TripSearchRequest req, CancellationToken ct)
    {
        var minutes = (int)(toUtc - fromUtc).TotalMinutes;
        if (minutes <= 0) return;
        var overnight = IsOvernightLocal(at.Location, fromUtc, toUtc);
        if (hotelOvernights && overnight && minutes >= OvernightHotelThresholdMinutes)
        {
            var night = DateOnly.FromDateTime(at.Location.ToLocal(fromUtc));
            var stays = await _hotels.SearchAsync(at, night, ct);
            var stay = stays.FirstOrDefault();
            if (stay is not null)
            {
                segments.Add(new ItinerarySegment
                {
                    Mode = SegmentMode.HotelStay, From = at, To = at,
                    DepartUtc = fromUtc, ArriveUtc = toUtc,
                    Price = stay.PricePerNight, Carrier = stay.ProviderName, Note = stay.HotelName,
                });
                return;
            }
        }
        segments.Add(new ItinerarySegment
        {
            Mode = SegmentMode.Wait, From = at, To = at,
            DepartUtc = fromUtc, ArriveUtc = toUtc,
            Note = overnight ? "Overnight wait" : "Connection wait",
        });
    }

    private static bool IsOvernightLocal(Location loc, DateTime fromUtc, DateTime toUtc)
    {
        var fromLocal = loc.ToLocal(fromUtc);
        var toLocal = loc.ToLocal(toUtc);
        // Spans any part of 23:00–05:00 local, or rolls the date with 4h+ of night.
        for (var t = fromLocal; t < toLocal; t = t.AddMinutes(30))
            if (t.Hour >= 23 || t.Hour < 5) return true;
        return false;
    }

    private Itinerary Finalize(
        TripSearchRequest req, List<ItinerarySegment> segments, List<Transfer> transfers,
        List<TransportNode> flightPath, List<TransportNode> approach)
    {
        var currency = req.Currency;
        var total = Money.Zero(currency);
        foreach (var s in segments)
        {
            if (s.Price is null) continue;
            var perTraveller = s.Mode is SegmentMode.HotelStay
                ? s.Price with { Amount = s.Price.Amount * Math.Ceiling(req.Travellers / 2.0m) }   // rooms sleep 2
                : s.Price with { Amount = s.Price.Amount * req.Travellers };
            total = total.Plus(_fx.Convert(perTraveller, currency));
        }

        // Door-to-door = departure → standing at the gate ready to enter (the activity
        // start). Because entry is the first feasible slot after arrival, a faster
        // journey genuinely yields a smaller number here.
        var arrivalAtGate = segments.Last(s => s.Mode == SegmentMode.Activity).DepartUtc;
        var durationMin = (int)(arrivalAtGate - segments[0].DepartUtc).TotalMinutes;
        var walking = segments.Where(s => s.Mode == SegmentMode.Walking).Sum(s => s.DurationMinutes);
        var waiting = segments.Where(s => s.Mode == SegmentMode.Wait).Sum(s => s.DurationMinutes);
        var overnightAirportWaits = segments.Count(s =>
            s.Mode == SegmentMode.Wait && s.From.Kind == NodeKind.Airport &&
            IsOvernightLocal(s.From.Location, s.DepartUtc, s.ArriveUtc));
        var moveSegments = segments.Where(s => s.Mode is not (SegmentMode.Wait or SegmentMode.Checkpoint or SegmentMode.HotelStay or SegmentMode.Activity)).ToList();
        var transferCount = Math.Max(0, moveSegments.Count - 1);

        var risk = 0.0;
        foreach (var t in transfers)
        {
            if (t.WaitMinutes < 45) risk += 0.25;
            else if (t.WaitMinutes < 75) risk += 0.12;
            if (t.IsSelfTransfer) risk += 0.20;
        }
        risk += segments.Count(s => s.CrossesBorder && s.IsFlight) * 0.02;
        risk = Math.Clamp(risk, 0, 1);

        var busHours = segments.Where(s => s.Mode == SegmentMode.IntercityBus).Sum(s => s.DurationMinutes) / 60.0;
        var comfort = 1.0
            - 0.06 * transferCount
            - 0.12 * overnightAirportWaits
            - walking / 600.0
            - (busHours > 4 ? 0.08 : 0);
        comfort = Math.Clamp(comfort, 0, 1);

        var carbon = 0.0;
        foreach (var s in moveSegments)
        {
            var km = Haversine.DistanceKm(s.From.Location, s.To.Location);
            carbon += km * s.Mode switch
            {
                SegmentMode.InternationalFlight => 0.115,
                SegmentMode.DomesticFlight => 0.133,
                SegmentMode.Train => 0.035,
                SegmentMode.IntercityBus or SegmentMode.UrbanTransit => 0.027,
                SegmentMode.Taxi or SegmentMode.PrivateTransfer or SegmentMode.AirportAccess => 0.17,
                SegmentMode.Ferry => 0.11,
                _ => 0,
            };
        }

        var viaCodes = flightPath.Skip(1).SkipLast(1).Select(n => n.Code);
        var approachTag = approach.Any(n => n.Code == "HIDRO") ? "trek" : "rail";
        return new Itinerary
        {
            Label = $"{flightPath[0].Code} via {string.Join("-", viaCodes)} ({approachTag})",
            Segments = segments,
            Transfers = transfers,
            TotalPrice = total with { Amount = Math.Round(total.Amount, 2) },
            TotalDurationMinutes = durationMin,
            TransferCount = transferCount,
            WalkingMinutes = walking,
            WaitingMinutes = waiting,
            OvernightWaits = overnightAirportWaits,
            RiskScore = Math.Round(risk, 3),
            ComfortScore = Math.Round(comfort, 3),
            CarbonKgEstimate = Math.Round(carbon * req.Travellers, 1),
            RequiredDocuments = new[] { "Passport (6+ months validity recommended — demo note)" },
        };
    }

    private static List<Itinerary> Dedupe(List<Itinerary> list) =>
        list.GroupBy(i => (i.TotalPrice.Amount, i.TotalDurationMinutes, i.TransferCount,
                           string.Join("|", i.Segments.Select(s => $"{s.Mode}:{s.From.Code}>{s.To.Code}"))))
            .Select(g => g.First())
            .ToList();

    private List<Itinerary> ApplyHardPreferences(TripSearchRequest req, List<Itinerary> candidates)
    {
        var result = candidates;
        if (req.MaxTransfers is { } cap)
        {
            var filtered = result.Where(i => i.TransferCount <= cap).ToList();
            if (filtered.Count > 0) result = filtered;
        }
        if (req.AvoidOvernightLayovers)
        {
            var filtered = result.Where(i => i.OvernightWaits == 0).ToList();
            if (filtered.Count > 0) result = filtered;
        }
        if (req.ReducedWalking)
        {
            var filtered = result.Where(i => i.WalkingMinutes <= 30).ToList();
            if (filtered.Count > 0) result = filtered;
        }
        return result;
    }
}
