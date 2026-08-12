using System.Text.Json;
using System.Text.Json.Serialization;
using Ai200Trainer.Models;
using Microsoft.JSInterop;

namespace Ai200Trainer.Services;

/// <summary>
/// Per-question and per-session history, kept in the visitor's own browser via
/// <c>localStorage</c>.
/// <para>
/// This deliberately does not live on the server. The app is deployed to a public URL,
/// and a server-side store would be a single record shared by everyone who opens it —
/// every visitor's answers would move the same accuracy figures and weak-topic list.
/// Browser storage gives each person their own history with no accounts and no database,
/// and it survives redeploys.
/// </para>
/// Registered as scoped, so there is one instance per Blazor circuit. Loading is async
/// because JS interop is unavailable until the circuit is connected — call
/// <see cref="EnsureLoadedAsync"/> from <c>OnAfterRenderAsync(firstRender: true)</c>.
/// </summary>
public sealed class ProgressStore(ExamContext exams, IJSRuntime js, ILogger<ProgressStore> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// History per exam slug. Studying two certifications must not blend their accuracy,
    /// weak-topic lists or exam dates, so every exam gets its own record.
    /// </summary>
    private readonly Dictionary<string, ProgressData> _byExam = [];
    private Task<bool>? _load;

    private string Slug => exams.Exam.Slug;

    private ProgressData _data
    {
        get
        {
            if (!_byExam.TryGetValue(Slug, out var data))
            {
                data = new ProgressData();
                _byExam[Slug] = data;
            }
            return data;
        }
    }

    /// <summary>False until the browser's stored history has been read in.</summary>
    public bool IsLoaded { get; private set; }

    public string StorageDescription => "This browser's local storage";

    /// <summary>
    /// Reads stored history from the browser. Safe to call from every component: the load
    /// runs once and every caller awaits the same operation.
    /// <para>
    /// Returns true when there was stored history to restore, which tells the caller its
    /// first render was based on empty data and should be repeated. The result is cached
    /// with the task on purpose — several components on a page call this, and if only the
    /// first one were told "yes, data arrived" the rest would keep showing zeroes.
    /// </para>
    /// </summary>
    public Task<bool> EnsureLoadedAsync() => _load ??= LoadAsync();

    private async Task<bool> LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("ai200Progress.load");

            if (string.IsNullOrWhiteSpace(json)) return false;

            // Stored shape is a map of exam slug -> history. Before multi-exam support it was
            // a single bare record, so anything in the old shape is migrated into AI-200
            // rather than discarded — people have real study history in there.
            using var doc = JsonDocument.Parse(json);
            var looksLikeOldSingleExam = doc.RootElement.ValueKind == JsonValueKind.Object
                                         && doc.RootElement.TryGetProperty("questions", out _);

            if (looksLikeOldSingleExam)
            {
                var legacy = JsonSerializer.Deserialize<ProgressData>(json, JsonOptions);
                if (legacy is null) return false;

                _byExam["ai-200"] = legacy;
                logger.LogInformation("Migrated single-exam progress into the ai-200 slot.");
                Save();
                return true;
            }

            var parsed = JsonSerializer.Deserialize<Dictionary<string, ProgressData>>(json, JsonOptions);
            if (parsed is null || parsed.Count == 0) return false;

            foreach (var (slug, data) in parsed) _byExam[slug] = data;
            return true;
        }
        catch (Exception ex)
        {
            // Corrupt or unreadable storage should never stop you from studying.
            logger.LogWarning(ex, "Could not read progress from browser storage; starting fresh.");
            return false;
        }
        finally
        {
            IsLoaded = true;
        }
    }

    public IReadOnlyDictionary<string, QuestionStat> Stats => _data.Questions;

    public IReadOnlyList<SessionRecord> Sessions => _data.Sessions;

    public DateOnly? ExamDate => _data.ExamDate;

    /// <summary>Whole days from today until the exam. Negative once the date has passed.</summary>
    public int? DaysUntilExam =>
        ExamDate is { } date ? date.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber : null;

    public void SetExamDate(DateOnly? date)
    {
        _data.ExamDate = date;
        Save();
    }

    public QuestionStat StatFor(string questionId) =>
        _data.Questions.TryGetValue(questionId, out var s) ? s : new QuestionStat();

    /// <summary>Records one graded answer.</summary>
    public void RecordAnswer(string questionId, bool correct)
    {
        Apply(questionId, correct);
        Save();
    }

    /// <summary>
    /// Records a batch of graded answers with a single write. Used when an exam is submitted,
    /// where writing once per question would mean fifty round trips to the browser.
    /// </summary>
    public void RecordAnswers(IEnumerable<(string QuestionId, bool Correct)> answers)
    {
        foreach (var (questionId, correct) in answers)
        {
            Apply(questionId, correct);
        }
        Save();
    }

    private void Apply(string questionId, bool correct)
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

    public void RecordSession(StudySession session)
    {
        _data.Sessions.Add(new SessionRecord
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
        });

        // Keep the record small enough to sit comfortably in localStorage.
        if (_data.Sessions.Count > 200)
        {
            _data.Sessions.RemoveRange(0, _data.Sessions.Count - 200);
        }

        Save();
    }

    /// <summary>Lifetime accuracy across every question in <paramref name="questions"/> that has been attempted.</summary>
    public (int Seen, int Correct, int Unseen) Coverage(IEnumerable<Question> questions)
    {
        var seen = 0;
        var correct = 0;
        var unseen = 0;

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

        return (seen, correct, unseen);
    }

    /// <summary>
    /// Clears answer history and session records for the current exam only, leaving any other
    /// exam's history alone. The exam date is deliberately kept.
    /// </summary>
    public void Reset()
    {
        _byExam[Slug] = new ProgressData { ExamDate = _data.ExamDate };
        Save();
    }

    /// <summary>
    /// Writes back to the browser. Fire-and-forget: the caller is a UI event handler and
    /// should not block on a round trip, and a failed write must not break the quiz.
    /// </summary>
    private void Save()
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(_byExam, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not serialise progress.");
            return;
        }

        _ = SaveAsync(json);
    }

    private async Task SaveAsync(string json)
    {
        try
        {
            await js.InvokeVoidAsync("ai200Progress.save", json);
        }
        catch (JSDisconnectedException)
        {
            // Circuit went away mid-write (tab closed). Nothing to do.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write progress to browser storage.");
        }
    }
}
