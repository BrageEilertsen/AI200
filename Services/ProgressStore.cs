using System.Text.Json;
using System.Text.Json.Serialization;
using Ai200Trainer.Models;

namespace Ai200Trainer.Services;

/// <summary>
/// Per-question and per-session history, persisted as JSON under
/// <c>%LOCALAPPDATA%\Ai200Trainer\progress.json</c> so it survives rebuilds of the app.
/// </summary>
public sealed class ProgressStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ILogger<ProgressStore> _logger;
    private readonly Lock _gate = new();
    private ProgressData _data = new();

    public string FilePath { get; }

    public ProgressStore(ILogger<ProgressStore> logger)
    {
        _logger = logger;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ai200Trainer");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "progress.json");

        Load();
    }

    public IReadOnlyDictionary<string, QuestionStat> Stats
    {
        get { lock (_gate) return _data.Questions; }
    }

    public IReadOnlyList<SessionRecord> Sessions
    {
        get { lock (_gate) return _data.Sessions; }
    }

    private void Load()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            var loaded = JsonSerializer.Deserialize<ProgressData>(File.ReadAllText(FilePath), JsonOptions);
            if (loaded is not null)
            {
                lock (_gate) _data = loaded;
            }
        }
        catch (Exception ex)
        {
            // A corrupt progress file should never stop you from studying.
            _logger.LogWarning(ex, "Could not read progress from {Path}; starting fresh.", FilePath);
        }
    }

    private void Save()
    {
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_data, JsonOptions);

            // Write to a temp file first so an interrupted write cannot truncate the real one.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save progress to {Path}.", FilePath);
        }
    }

    /// <summary>Records one graded answer. Called as you go in practice, and in bulk at the end of an exam.</summary>
    public void RecordAnswer(string questionId, bool correct)
    {
        lock (_gate)
        {
            if (!_data.Questions.TryGetValue(questionId, out var stat))
            {
                stat = new QuestionStat();
                _data.Questions[questionId] = stat;
            }

            stat.Seen++;
            if (correct) stat.Correct++;
            stat.LastWasCorrect = correct;
            stat.LastSeen = DateTimeOffset.Now;
        }

        Save();
    }

    public void RecordSession(StudySession session)
    {
        var record = new SessionRecord
        {
            FinishedAt = DateTimeOffset.Now,
            Mode = session.Mode,
            Total = session.Total,
            Correct = session.CorrectCount,
            ScaledScore = session.EstimatedScaledScore,
            ElapsedMinutes = Math.Round(session.Elapsed.TotalMinutes, 1),
            ByDomain = session.ByDomain().ToDictionary(
                kv => kv.Key,
                kv => new DomainTally { Correct = kv.Value.Correct, Total = kv.Value.Total })
        };

        lock (_gate)
        {
            _data.Sessions.Add(record);

            // Keep the history readable; the charts only look at the recent run anyway.
            if (_data.Sessions.Count > 200)
            {
                _data.Sessions.RemoveRange(0, _data.Sessions.Count - 200);
            }
        }

        Save();
    }

    public DateOnly? ExamDate
    {
        get { lock (_gate) return _data.ExamDate; }
    }

    public void SetExamDate(DateOnly? date)
    {
        lock (_gate) _data.ExamDate = date;
        Save();
    }

    /// <summary>Whole days from today until the exam. Negative once the date has passed.</summary>
    public int? DaysUntilExam =>
        ExamDate is { } date ? date.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber : null;

    public QuestionStat StatFor(string questionId) =>
        Stats.TryGetValue(questionId, out var s) ? s : new QuestionStat();

    /// <summary>Lifetime accuracy across every question in <paramref name="questions"/> that has been attempted.</summary>
    public (int Seen, int Correct, int Unseen) Coverage(IEnumerable<Question> questions)
    {
        var seen = 0;
        var correct = 0;
        var unseen = 0;

        lock (_gate)
        {
            foreach (var q in questions)
            {
                if (_data.Questions.TryGetValue(q.Id, out var stat) && stat.Seen > 0)
                {
                    seen += stat.Seen;
                    correct += stat.Correct;
                }
                else
                {
                    unseen++;
                }
            }
        }

        return (seen, correct, unseen);
    }

    /// <summary>Clears answer history and session records. The exam date is deliberately kept.</summary>
    public void Reset()
    {
        lock (_gate) _data = new ProgressData { ExamDate = _data.ExamDate };
        Save();
    }
}
