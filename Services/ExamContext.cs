using Ai200Trainer.Models;
using Microsoft.JSInterop;

namespace Ai200Trainer.Services;

/// <summary>
/// Which exam this visitor is studying, and the source of questions for it.
/// <para>
/// Scoped to the Blazor circuit. The choice is remembered in the browser, so it survives a
/// reload and is per person rather than per server — the same reasoning as
/// <see cref="ProgressStore"/>.
/// </para>
/// </summary>
public sealed class ExamContext(ExamCatalog catalog, IJSRuntime js, ILogger<ExamContext> logger)
{
    private ExamBank? _current;
    private Task<bool>? _load;

    /// <summary>Fires when the visitor switches exam, so open pages can reset.</summary>
    public event Action? Changed;

    public ExamBank Bank => _current ?? catalog.Default
        ?? throw new InvalidOperationException("No exams are loaded. Check the Data directory.");

    public ExamDefinition Exam => Bank.Definition;

    public IReadOnlyList<ExamBank> All => catalog.Exams;

    public bool HasMultipleExams => catalog.Exams.Count > 1;

    /// <summary>
    /// Restores the remembered exam. Safe to call from every component; the read runs once
    /// and every caller awaits the same operation. Returns true when the restored exam
    /// differs from the default, meaning the caller's first render was for the wrong exam.
    /// </summary>
    public Task<bool> EnsureLoadedAsync() => _load ??= LoadAsync();

    private async Task<bool> LoadAsync()
    {
        try
        {
            var slug = await js.InvokeAsync<string?>("ai200Exam.get");
            if (string.IsNullOrWhiteSpace(slug)) return false;

            var found = catalog.Find(slug);
            if (found is null || ReferenceEquals(found, catalog.Default)) return false;

            _current = found;

            // Restoring is a switch as far as the rest of the app is concerned: every page
            // rendered its first pass against the default exam. Components that hold state
            // derived from the bank — selected domains, filters — need to hear about it, not
            // only the ones that happen to inspect this method's return value.
            Changed?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the selected exam; falling back to the default.");
            return false;
        }
    }

    public async Task SelectAsync(string slug)
    {
        var found = catalog.Find(slug);
        if (found is null || ReferenceEquals(found, _current)) return;

        _current = found;

        try
        {
            await js.InvokeVoidAsync("ai200Exam.set", slug);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remember the selected exam.");
        }

        Changed?.Invoke();
    }

    // ---- drawing ----------------------------------------------------------------

    /// <summary>
    /// Draws <paramref name="count"/> questions from the current exam, narrowed to the given
    /// skill areas and — for exams with a published syllabus — learning paths. An empty
    /// collection means no restriction on that axis.
    /// When <paramref name="focusWeak"/> is set, questions never seen or recently missed are
    /// strongly favoured; otherwise the draw is uniformly random.
    /// </summary>
    public List<Question> Draw(
        IReadOnlyCollection<string> domainKeys,
        IReadOnlyCollection<string> pathKeys,
        int count,
        bool focusWeak,
        IReadOnlyDictionary<string, QuestionStat>? stats = null)
    {
        var pool = Bank.Questions
            .Where(q => domainKeys.Count == 0 || domainKeys.Contains(q.Domain))
            .Where(q => pathKeys.Count == 0 || (q.Path is not null && pathKeys.Contains(q.Path)))
            .ToList();

        return Present(Pick(pool, count, focusWeak, stats));
    }

    /// <summary>
    /// Builds a full mock: <paramref name="count"/> questions split across the exam's domains
    /// in proportion to the published weights. Falls back to filling from the remaining pool
    /// if a domain does not have enough questions to cover its share.
    /// </summary>
    public List<Question> DrawExam(
        int count,
        bool focusWeak = false,
        IReadOnlyDictionary<string, QuestionStat>? stats = null)
    {
        var chosen = new List<Question>();

        foreach (var (domainKey, want) in Exam.Allocate(count))
        {
            var pool = Bank.Questions.Where(q => q.Domain == domainKey).ToList();
            chosen.AddRange(Pick(pool, want, focusWeak, stats));
        }

        if (chosen.Count < count)
        {
            var taken = chosen.Select(q => q.Id).ToHashSet();
            var filler = Bank.Questions.Where(q => !taken.Contains(q.Id)).ToList();
            chosen.AddRange(Pick(filler, count - chosen.Count, focusWeak, stats));
        }

        return Present(Shuffle(chosen));
    }

    private static List<Question> Pick(
        List<Question> pool,
        int count,
        bool focusWeak,
        IReadOnlyDictionary<string, QuestionStat>? stats)
    {
        if (count <= 0 || pool.Count == 0) return [];
        if (pool.Count <= count) return Shuffle(pool);

        if (!focusWeak || stats is null) return [.. Shuffle(pool).Take(count)];

        // Weighted-random by priority so repeated "focus weak" runs are not identical.
        var ranked = pool
            .OrderByDescending(q =>
            {
                var priority = stats.TryGetValue(q.Id, out var s) ? s.Priority : 100;
                return priority * (0.7 + Random.Shared.NextDouble() * 0.6);
            })
            .Take(count);

        return Shuffle(ranked);
    }

    /// <summary>
    /// Final step before questions leave the bank: hand back copies with the answer choices in
    /// a random order. Every draw goes through here so no path can serve them in the authored
    /// order, where the correct answer sits at A far too often.
    /// </summary>
    private static List<Question> Present(List<Question> questions) =>
        [.. questions.Select(q => q.WithShuffledOptions(Random.Shared))];

    private static List<T> Shuffle<T>(IEnumerable<T> source)
    {
        var list = source.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
