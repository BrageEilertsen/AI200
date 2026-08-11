namespace Ai200Trainer.Models;

/// <summary>
/// One of the four "skills at a glance" areas from the official AI-200 study guide.
/// Weights are the published ranges; <see cref="Weight"/> is the midpoint, which is
/// what exam simulation uses to decide how many questions to draw from each area.
/// </summary>
public sealed record ExamDomain(
    string Key,
    string Name,
    string ShortName,
    int MinWeight,
    int MaxWeight,
    string Accent)
{
    public double Weight => (MinWeight + MaxWeight) / 2.0;

    public string WeightLabel => $"{MinWeight}–{MaxWeight}%";
}

public static class ExamDomains
{
    public static readonly IReadOnlyList<ExamDomain> All =
    [
        new("containers",
            "Develop containerized solutions on Azure",
            "Containers",
            20, 25, "var(--d-containers)"),

        new("data",
            "Develop AI solutions by using Azure data management services",
            "AI data services",
            25, 30, "var(--d-data)"),

        new("integration",
            "Connect to and consume Azure services",
            "Messaging & Functions",
            20, 25, "var(--d-integration)"),

        new("operations",
            "Secure, monitor, and troubleshoot Azure solutions",
            "Security & observability",
            20, 25, "var(--d-operations)")
    ];

    public static ExamDomain Get(string key) =>
        All.FirstOrDefault(d => d.Key == key)
        ?? new ExamDomain(key, key, key, 0, 0, "var(--text-3)");

    /// <summary>
    /// Splits <paramref name="total"/> questions across the domains in proportion to the
    /// published exam weights, handing any rounding remainder to the heaviest domains first.
    /// </summary>
    public static Dictionary<string, int> Allocate(int total)
    {
        var sum = All.Sum(d => d.Weight);
        var exact = All.ToDictionary(d => d.Key, d => d.Weight / sum * total);
        var result = exact.ToDictionary(kv => kv.Key, kv => (int)Math.Floor(kv.Value));

        var remainder = total - result.Values.Sum();
        foreach (var key in exact.OrderByDescending(kv => kv.Value - Math.Floor(kv.Value))
                                 .Select(kv => kv.Key)
                                 .Take(remainder))
        {
            result[key]++;
        }

        return result;
    }
}
