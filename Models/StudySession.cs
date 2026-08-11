namespace Ai200Trainer.Models;

public enum SessionMode
{
    /// <summary>Answer, check, see the explanation immediately, move on.</summary>
    Practice,

    /// <summary>Timed, weighted to the real exam, no feedback until you submit the whole set.</summary>
    Exam
}

public sealed class SessionItem
{
    public required Question Question { get; init; }

    public List<string> Selected { get; } = [];

    /// <summary>True once the answer has been locked in and graded.</summary>
    public bool Submitted { get; set; }

    public bool Flagged { get; set; }

    public bool Answered => Selected.Count > 0;

    public bool IsCorrect => Question.IsCorrect(Selected);

    public void Toggle(string optionId)
    {
        if (Submitted) return;

        if (Question.Kind == QuestionKind.Single)
        {
            Selected.Clear();
            Selected.Add(optionId);
            return;
        }

        if (!Selected.Remove(optionId))
        {
            Selected.Add(optionId);
        }
    }
}

public sealed class StudySession
{
    public required SessionMode Mode { get; init; }
    public required List<SessionItem> Items { get; init; }
    public TimeSpan? TimeLimit { get; init; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public int Index { get; set; }
    public bool Finished { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public SessionItem Current => Items[Index];
    public int Total => Items.Count;
    public int AnsweredCount => Items.Count(i => i.Answered);
    public int CorrectCount => Items.Count(i => i.Submitted && i.IsCorrect);
    public int FlaggedCount => Items.Count(i => i.Flagged);

    public TimeSpan Elapsed => (FinishedAt ?? DateTimeOffset.Now) - StartedAt;

    public TimeSpan? Remaining =>
        TimeLimit is { } limit ? limit - Elapsed : null;

    public bool TimeExpired => Remaining is { } r && r <= TimeSpan.Zero;

    public bool CanGoNext => Index < Total - 1;
    public bool CanGoPrevious => Index > 0;

    public double ScorePercent => Total == 0 ? 0 : 100.0 * CorrectCount / Total;

    /// <summary>
    /// Rough stand-in for the Microsoft scaled score. Microsoft does not publish its
    /// scaling curve, so this is a linear map where 70% lands on the 700 pass mark.
    /// Treat it as a signal, not a prediction.
    /// </summary>
    public int EstimatedScaledScore => (int)Math.Round(ScorePercent * 10);

    public bool Passed => EstimatedScaledScore >= 700;

    /// <summary>Grades everything that is still open. Used when an exam run is submitted or times out.</summary>
    public void Finish()
    {
        if (Finished) return;

        foreach (var item in Items)
        {
            item.Submitted = true;
        }

        Finished = true;
        FinishedAt = DateTimeOffset.Now;
    }

    public IEnumerable<SessionItem> Missed => Items.Where(i => i.Submitted && !i.IsCorrect);

    public Dictionary<string, (int Correct, int Total)> ByDomain()
    {
        var map = new Dictionary<string, (int Correct, int Total)>();
        foreach (var item in Items)
        {
            var key = item.Question.Domain;
            var (correct, total) = map.GetValueOrDefault(key);
            map[key] = (correct + (item.Submitted && item.IsCorrect ? 1 : 0), total + 1);
        }
        return map;
    }

    public Dictionary<string, (int Correct, int Total)> ByObjective()
    {
        var map = new Dictionary<string, (int Correct, int Total)>();
        foreach (var item in Items)
        {
            var key = item.Question.Objective;
            var (correct, total) = map.GetValueOrDefault(key);
            map[key] = (correct + (item.Submitted && item.IsCorrect ? 1 : 0), total + 1);
        }
        return map;
    }
}
