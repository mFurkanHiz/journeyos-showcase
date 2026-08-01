namespace JourneyOS.Showcase.Domain;

public static class Haversine
{
    private const double EarthRadiusKm = 6371.0088;

    public static double DistanceKm(Location a, Location b) => DistanceKm(a.Lat, a.Lon, b.Lat, b.Lon);

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var p1 = lat1 * Math.PI / 180.0;
        var p2 = lat2 * Math.PI / 180.0;
        var dp = (lat2 - lat1) * Math.PI / 180.0;
        var dl = (lon2 - lon1) * Math.PI / 180.0;
        var h = Math.Sin(dp / 2) * Math.Sin(dp / 2)
              + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }
}
