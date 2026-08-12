using Ai200Trainer.Models;

namespace Ai200Trainer.Services;

/// <summary>
/// Holds the run in progress. Scoped, so it lives for the lifetime of the Blazor circuit —
/// you can navigate between pages mid-quiz and come back to where you were.
/// </summary>
public sealed class SessionHost(QuestionBank bank, ProgressStore progress)
{
    public StudySession? Current { get; private set; }

    public bool HasActiveRun => Current is { Finished: false };

    public StudySession StartPractice(
        IReadOnlyCollection<string> domains,
        int count,
        bool focusWeak,
        bool retryMissed = false)
    {
        var questions = bank.Draw(domains, count, focusWeak, progress.Stats);
        Current = new StudySession
        {
            Mode = SessionMode.Practice,
            Items = [.. questions.Select(q => new SessionItem { Question = q })],
            RetryMissed = retryMissed
        };
        return Current;
    }

    public StudySession StartExam(int count, int minutes, bool focusWeak)
    {
        var questions = bank.DrawExam(count, focusWeak, progress.Stats);
        Current = new StudySession
        {
            Mode = SessionMode.Exam,
            Items = [.. questions.Select(q => new SessionItem { Question = q })],
            TimeLimit = TimeSpan.FromMinutes(minutes)
        };
        return Current;
    }

    /// <summary>Grades the current question in a practice run and folds the result into lifetime stats.</summary>
    public void SubmitCurrent()
    {
        if (Current is not { } session) return;

        var item = session.Current;
        if (item.Submitted || !item.Answered) return;

        item.Submitted = true;
        progress.RecordAnswer(item.Question.Id, item.IsCorrect);

        // Getting it wrong once is exactly when a second look is worth most, so put the
        // question back at the end of the set rather than only in the review list.
        if (!item.IsCorrect) session.QueueRetry(item);
    }

    /// <summary>Ends the run, grading anything still open and writing the session record.</summary>
    public void Finish()
    {
        if (Current is not { Finished: false } session) return;

        // In exam mode nothing has been graded yet, so fold every item in now.
        // In practice mode the answered ones are already recorded — only unanswered
        // stragglers (skipped at the end) still need counting.
        var alreadyRecorded = session.Items.Where(i => i.Submitted).Select(i => i.Question.Id).ToHashSet();

        session.Finish();

        var pending = session.Items
            .Where(i => session.Mode != SessionMode.Practice || !alreadyRecorded.Contains(i.Question.Id))
            .Select(i => (i.Question.Id, i.IsCorrect));

        // One batched write rather than one per question — each write is a round trip
        // to the browser, and an exam submits fifty answers at once.
        progress.RecordAnswers(pending);

        progress.RecordSession(session);
    }

    public void Clear() => Current = null;
}
