namespace Ai200Trainer.Models;

/// <summary>Lifetime record for a single question, used to drive the "focus weak areas" picker.</summary>
public sealed class QuestionStat
{
    public int Seen { get; set; }
    public int Correct { get; set; }
    public bool LastWasCorrect { get; set; }
    public DateTimeOffset? LastSeen { get; set; }

    public double Accuracy => Seen == 0 ? 0 : (double)Correct / Seen;

    /// <summary>
    /// Higher = more worth re-asking. Never-seen questions rank highest, then ones you got
    /// wrong last time, then ones with a poor lifetime record. Recently-correct ranks lowest.
    /// </summary>
    public double Priority
    {
        get
        {
            if (Seen == 0) return 100;

            var score = 50 * (1 - Accuracy);
            if (!LastWasCorrect) score += 30;

            // Decay: something you got right a week ago is worth revisiting sooner than one from today.
            var daysSince = LastSeen is { } t ? (DateTimeOffset.Now - t).TotalDays : 30;
            score += Math.Min(daysSince * 2, 20);

            return score;
        }
    }
}

public sealed class DomainTally
{
    public int Correct { get; set; }
    public int Total { get; set; }
}

public sealed class SessionRecord
{
    public DateTimeOffset FinishedAt { get; set; }
    public SessionMode Mode { get; set; }
    public int Total { get; set; }
    public int Correct { get; set; }
    public int ScaledScore { get; set; }
    public double ElapsedMinutes { get; set; }
    public Dictionary<string, DomainTally> ByDomain { get; set; } = [];

    public double Percent => Total == 0 ? 0 : 100.0 * Correct / Total;
}

public sealed class ProgressData
{
    public Dictionary<string, QuestionStat> Questions { get; set; } = [];
    public List<SessionRecord> Sessions { get; set; } = [];

    /// <summary>Scheduled exam date, used for the countdown in the sidebar.</summary>
    public DateOnly? ExamDate { get; set; }
}
