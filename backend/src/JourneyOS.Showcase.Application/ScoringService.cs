using JourneyOS.Showcase.Domain;

namespace JourneyOS.Showcase.Application;

/// <summary>Profile scoring over a candidate set. Every metric is min-max
/// normalized ACROSS THE CANDIDATES (0 = best in set, 1 = worst), then weighted per
/// profile — so "Fastest", "Cheapest" and "Balanced" are real, different decisions
/// with an auditable breakdown, not the same list re-sorted (docs/scoring.md).</summary>
public sealed class ScoringService
{
    /// <summary>Weights per profile. LOWER total = better (all metrics are costs).
    /// Each row sums to 1.0 (asserted by tests).</summary>
    public static readonly IReadOnlyDictionary<OptimizationProfile, IReadOnlyDictionary<string, double>> Weights =
        new Dictionary<OptimizationProfile, IReadOnlyDictionary<string, double>>
        {
            [OptimizationProfile.Fastest] = new Dictionary<string, double>
            {
                ["price"] = 0.05, ["duration"] = 0.55, ["transfers"] = 0.10, ["risk"] = 0.10,
                ["waiting"] = 0.10, ["overnight"] = 0.05, ["comfort"] = 0.03, ["walking"] = 0.02,
            },
            [OptimizationProfile.Cheapest] = new Dictionary<string, double>
            {
                ["price"] = 0.60, ["duration"] = 0.08, ["transfers"] = 0.05, ["risk"] = 0.07,
                ["waiting"] = 0.05, ["overnight"] = 0.05, ["comfort"] = 0.05, ["walking"] = 0.05,
            },
            [OptimizationProfile.Balanced] = new Dictionary<string, double>
            {
                ["price"] = 0.25, ["duration"] = 0.25, ["transfers"] = 0.12, ["risk"] = 0.12,
                ["waiting"] = 0.08, ["overnight"] = 0.08, ["comfort"] = 0.06, ["walking"] = 0.04,
            },
        };

    /// <summary>Picks the winner for each profile and attaches its breakdown +
    /// human explanation. Returns profile → winning itinerary (winners can repeat
    /// when one candidate dominates — that is honest, not a bug).</summary>
    public IReadOnlyDictionary<OptimizationProfile, Itinerary> PickPerProfile(IReadOnlyList<Itinerary> candidates)
    {
        if (candidates.Count == 0) throw new ArgumentException("empty candidate set");
        var metrics = candidates.ToDictionary(c => c.Id, c => RawMetrics(c));
        var normalized = Normalize(metrics);

        var result = new Dictionary<OptimizationProfile, Itinerary>();
        var fastest = candidates.OrderBy(c => c.TotalDurationMinutes).First();
        foreach (var (profile, weights) in Weights)
        {
            Itinerary? best = null;
            var bestTotal = double.MaxValue;
            foreach (var c in candidates)
            {
                var total = weights.Sum(w => normalized[c.Id][w.Key] * w.Value);
                if (total < bestTotal) { bestTotal = total; best = c; }
            }
            var components = weights
                .Select(w => new ScoreComponent(w.Key, Math.Round(normalized[best!.Id][w.Key], 4), w.Value))
                .ToList();
            best!.Score = new ScoreBreakdown(profile, components, Explain(profile, best, fastest));
            result[profile] = best;
        }
        return result;
    }

    private static Dictionary<string, double> RawMetrics(Itinerary c) => new()
    {
        ["price"] = (double)c.TotalPrice.Amount,
        ["duration"] = c.TotalDurationMinutes,
        ["transfers"] = c.TransferCount,
        ["risk"] = c.RiskScore,
        ["waiting"] = c.WaitingMinutes,
        ["overnight"] = c.OvernightWaits,
        ["comfort"] = 1 - c.ComfortScore,    // scoring treats everything as a cost
        ["walking"] = c.WalkingMinutes,
    };

    /// <summary>Min-max per metric across the set; a metric with no spread scores 0
    /// for everyone (it cannot differentiate, so it must not punish).</summary>
    private static Dictionary<Guid, Dictionary<string, double>> Normalize(
        Dictionary<Guid, Dictionary<string, double>> raw)
    {
        var keys = raw.Values.First().Keys.ToList();
        var result = raw.Keys.ToDictionary(id => id, _ => new Dictionary<string, double>());
        foreach (var key in keys)
        {
            var values = raw.Values.Select(m => m[key]).ToList();
            var min = values.Min();
            var max = values.Max();
            var spread = max - min;
            foreach (var (id, m) in raw)
                result[id][key] = spread < 1e-9 ? 0 : (m[key] - min) / spread;
        }
        return result;
    }

    private static string Explain(OptimizationProfile profile, Itinerary chosen, Itinerary fastest)
    {
        if (chosen.Id == fastest.Id)
            return profile switch
            {
                OptimizationProfile.Fastest => "The shortest door-to-door option in this set.",
                _ => "This option leads its profile AND is also the fastest in the set.",
            };
        var extraH = Math.Round((chosen.TotalDurationMinutes - fastest.TotalDurationMinutes) / 60.0, 1);
        var parts = new List<string> { $"{extraH}h longer than the fastest option" };
        var fewerTransfers = fastest.TransferCount - chosen.TransferCount;
        if (fewerTransfers > 0) parts.Add($"{fewerTransfers} fewer transfer{(fewerTransfers > 1 ? "s" : "")}");
        if (chosen.TotalPrice.Amount < fastest.TotalPrice.Amount)
            parts.Add($"saves {Math.Round(fastest.TotalPrice.Amount - chosen.TotalPrice.Amount)} {chosen.TotalPrice.Currency}");
        if (chosen.OvernightWaits < fastest.OvernightWaits)
            parts.Add("no overnight airport wait");
        if (chosen.RiskScore < fastest.RiskScore)
            parts.Add("safer connections");
        return string.Join(", but ", new[] { parts[0], string.Join(" and ", parts.Skip(1)) }
            .Where(s => s.Length > 0)) + ".";
    }
}
