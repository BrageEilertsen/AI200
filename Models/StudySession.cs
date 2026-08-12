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

    /// <summary>True when this is a second attempt, queued after the first was missed.</summary>
    public bool IsRetry { get; init; }

    public List<string> Selected { get; } = [];

    /// <summary>True once the answer has been locked in and graded.</summary>
    public bool Submitted { get; set; }

    public bool Flagged { get; set; }

    /// <summary>
    /// True once enough has been selected to count as a complete attempt. Ordering and
    /// Blanks need every slot filled; the others just need one selection.
    /// </summary>
    public bool Answered => Question.Kind is QuestionKind.Ordering or QuestionKind.Blanks
        ? Selected.Count >= Question.RequiredSelections
        : Selected.Count > 0;

    /// <summary>Any selection at all — used to show partial progress on ordering questions.</summary>
    public bool Started => Selected.Count > 0;

    public bool IsCorrect => Question.IsCorrect(Selected);

    public void Toggle(string optionId)
    {
        if (Submitted) return;

        switch (Question.Kind)
        {
            case QuestionKind.Single:
                Selected.Clear();
                Selected.Add(optionId);
                break;

            // Clicking appends to the sequence; clicking an already-placed item pulls it
            // back out, and everything after it closes up.
            case QuestionKind.Ordering:
                if (!Selected.Remove(optionId)) Selected.Add(optionId);
                break;

            default:
                if (!Selected.Remove(optionId)) Selected.Add(optionId);
                break;
        }
    }

    /// <summary>Sets one dropdown's answer, replacing whatever that blank held before.</summary>
    public void SetBlank(string blankId, string? choiceId)
    {
        if (Submitted) return;

        Selected.RemoveAll(s => s.StartsWith(blankId + "=", StringComparison.Ordinal));

        if (!string.IsNullOrEmpty(choiceId))
        {
            Selected.Add(Question.BlankToken(blankId, choiceId));
        }
    }

    /// <summary>The choice currently picked for a blank, or null.</summary>
    public string? ChoiceFor(string blankId)
    {
        var prefix = blankId + "=";
        var hit = Selected.FirstOrDefault(s => s.StartsWith(prefix, StringComparison.Ordinal));
        return hit?[prefix.Length..];
    }

    /// <summary>1-based position of an option in the ordering answer, or null if unplaced.</summary>
    public int? PositionOf(string optionId)
    {
        var index = Selected.IndexOf(optionId);
        return index < 0 ? null : index + 1;
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

    /// <summary>Seconds available per question if the time is spent evenly. Null when untimed.</summary>
    public double? SecondsPerQuestion =>
        TimeLimit is { } limit && Total > 0 ? limit.TotalSeconds / Total : null;

    /// <summary>
    /// How many questions ahead of (positive) or behind (negative) the even pace you are.
    /// Compares questions answered against how many the elapsed time allows for.
    /// </summary>
    public double? PaceDelta =>
        TimeLimit is { } limit && limit > TimeSpan.Zero && Total > 0
            ? AnsweredCount - Elapsed / limit * Total
            : null;

    /// <summary>Set when a missed practice question should be asked again later in the same set.</summary>
    public bool RetryMissed { get; init; }

    /// <summary>Queues a second attempt at a missed question, once only.</summary>
    public void QueueRetry(SessionItem item)
    {
        if (!RetryMissed || item.IsRetry) return;

        Items.Add(new SessionItem { Question = item.Question, IsRetry = true });
    }

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
