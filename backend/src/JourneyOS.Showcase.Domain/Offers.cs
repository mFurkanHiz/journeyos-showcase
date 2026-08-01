namespace JourneyOS.Showcase.Domain;

/// <summary>Canonical provider results. Every adapter — mock today, a real GDS or
/// rail API tomorrow — must translate its own wire format into THESE records before
/// anything downstream sees the data. External DTOs never cross this line, which is
/// what makes providers swappable (see docs/canonical-model.md).</summary>
public abstract record ProviderOffer(string ProviderName);

/// <summary>A bookable movement between two nodes at a concrete time and price.</summary>
public sealed record TransportOffer(
    string ProviderName,
    SegmentMode Mode,
    TransportNode From,
    TransportNode To,
    DateTime DepartUtc,
    DateTime ArriveUtc,
    Money Price,
    string Carrier,
    bool IsSeparateTicket = false) : ProviderOffer(ProviderName)
{
    public int DurationMinutes => (int)Math.Round((ArriveUtc - DepartUtc).TotalMinutes);
}

/// <summary>A night (or more) in a bed near a node.</summary>
public sealed record StayOffer(
    string ProviderName,
    TransportNode At,
    string HotelName,
    Money PricePerNight) : ProviderOffer(ProviderName);

/// <summary>An entry ticket / guided slot at the destination.</summary>
public sealed record ActivityOffer(
    string ProviderName,
    TransportNode At,
    string Title,
    int DurationMinutes,
    Money Price) : ProviderOffer(ProviderName);
