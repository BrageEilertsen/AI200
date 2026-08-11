using System.Text.Json;
using System.Text.Json.Serialization;
using Ai200Trainer.Models;

namespace Ai200Trainer.Services;

/// <summary>
/// Loads the question bank from <c>Data/*.json</c> under the content root and hands out
/// question sets. Reload is cheap and non-destructive, so the JSON can be edited while
/// the app is running.
/// </summary>
public sealed class QuestionBank(IWebHostEnvironment env, ILogger<QuestionBank> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Lock _gate = new();
    private List<Question> _questions = [];
    private List<string> _problems = [];
    private Dictionary<string, CaseStudy> _caseStudies = [];

    public IReadOnlyList<Question> All
    {
        get { lock (_gate) return _questions; }
    }

    /// <summary>Shared case-study scenarios, keyed by id.</summary>
    public IReadOnlyDictionary<string, CaseStudy> CaseStudies
    {
        get { lock (_gate) return _caseStudies; }
    }

    public CaseStudy? CaseStudyFor(Question question) =>
        question.CaseStudyId is { } id && CaseStudies.TryGetValue(id, out var cs) ? cs : null;

    /// <summary>Validation messages from the last load. Surfaced on the Home screen.</summary>
    public IReadOnlyList<string> Problems
    {
        get { lock (_gate) return _problems; }
    }

    public DateTimeOffset LoadedAt { get; private set; }

    public string DataDirectory => Path.Combine(env.ContentRootPath, "Data");

    /// <summary>Scenario file, kept apart from the question files so its text is written once.</summary>
    private const string CaseStudyFile = "case-studies.json";

    public void Load()
    {
        var questions = new List<Question>();
        var caseStudies = new Dictionary<string, CaseStudy>();
        var problems = new List<string>();

        if (!Directory.Exists(DataDirectory))
        {
            problems.Add($"No Data directory found at {DataDirectory}.");
        }
        else
        {
            foreach (var path in Directory.EnumerateFiles(DataDirectory, "*.json").Order())
            {
                var name = Path.GetFileName(path);
                try
                {
                    if (name.Equals(CaseStudyFile, StringComparison.OrdinalIgnoreCase))
                    {
                        var studies = JsonSerializer.Deserialize<List<CaseStudy>>(File.ReadAllText(path), JsonOptions) ?? [];
                        foreach (var study in studies) caseStudies[study.Id] = study;
                        continue;
                    }

                    var parsed = JsonSerializer.Deserialize<List<Question>>(File.ReadAllText(path), JsonOptions);
                    if (parsed is null)
                    {
                        problems.Add($"{name}: file parsed to null.");
                        continue;
                    }
                    questions.AddRange(parsed);
                }
                catch (JsonException ex)
                {
                    problems.Add($"{name}: {ex.Message}");
                    logger.LogError(ex, "Failed to parse {File}", path);
                }
            }
        }

        // Blanks questions carry their answers on the blanks themselves; mirror them into
        // Correct so downstream code (counts, labels) does not need to special-case them.
        foreach (var q in questions.Where(q => q.Kind == QuestionKind.Blanks))
        {
            q.Correct = [.. q.Blanks.Select(b => Question.BlankToken(b.Id, b.Correct))];
        }

        problems.AddRange(Validate(questions, caseStudies));

        lock (_gate)
        {
            _questions = questions;
            _caseStudies = caseStudies;
            _problems = problems;
        }
        LoadedAt = DateTimeOffset.Now;

        logger.LogInformation(
            "Loaded {Count} questions and {Studies} case study/studies with {Problems} problem(s).",
            questions.Count, caseStudies.Count, problems.Count);
    }

    private static IEnumerable<string> Validate(
        List<Question> questions,
        Dictionary<string, CaseStudy> caseStudies)
    {
        foreach (var duplicate in questions.GroupBy(q => q.Id).Where(g => g.Count() > 1))
        {
            yield return $"Duplicate question id '{duplicate.Key}' appears {duplicate.Count()} times.";
        }

        foreach (var q in questions)
        {
            if (ExamDomains.All.All(d => d.Key != q.Domain))
            {
                yield return $"{q.Id}: unknown domain '{q.Domain}'.";
            }

            if (string.IsNullOrWhiteSpace(q.Explanation))
            {
                yield return $"{q.Id}: missing an explanation.";
            }

            if (q.CaseStudyId is { Length: > 0 } csId && !caseStudies.ContainsKey(csId))
            {
                yield return $"{q.Id}: references unknown case study '{csId}'.";
            }

            if (q.Kind == QuestionKind.Blanks)
            {
                if (q.Blanks.Count == 0)
                {
                    yield return $"{q.Id}: a blanks question needs at least one blank.";
                }

                foreach (var duplicate in q.Blanks.GroupBy(b => b.Id).Where(g => g.Count() > 1))
                {
                    yield return $"{q.Id}: duplicate blank id '{duplicate.Key}'.";
                }

                foreach (var b in q.Blanks)
                {
                    if (b.Choices.Count < 2)
                    {
                        yield return $"{q.Id}/{b.Id}: needs at least two choices.";
                    }
                    if (b.Choices.All(c => c.Id != b.Correct))
                    {
                        yield return $"{q.Id}/{b.Id}: correct choice '{b.Correct}' is not one of its choices.";
                    }
                }

                if (q.Options.Count > 0)
                {
                    yield return $"{q.Id}: a blanks question should not also define top-level options.";
                }

                continue;
            }

            if (q.Options.Count < 2)
            {
                yield return $"{q.Id}: needs at least two options.";
            }

            var optionIds = q.Options.Select(o => o.Id).ToHashSet();
            foreach (var missing in q.Correct.Where(c => !optionIds.Contains(c)))
            {
                yield return $"{q.Id}: correct answer '{missing}' is not one of the options.";
            }

            switch (q.Kind)
            {
                case QuestionKind.Single when q.Correct.Count != 1:
                    yield return $"{q.Id}: single-answer question has {q.Correct.Count} correct answers.";
                    break;

                case QuestionKind.Multi when q.Correct.Count < 2:
                    yield return $"{q.Id}: multi-answer question has fewer than two correct answers.";
                    break;

                // An ordering answer is the whole sequence, so every step must appear exactly once.
                case QuestionKind.Ordering when q.Correct.Count != q.Options.Count:
                    yield return $"{q.Id}: ordering question lists {q.Correct.Count} steps but has {q.Options.Count} options.";
                    break;

                case QuestionKind.Ordering when q.Correct.Distinct().Count() != q.Correct.Count:
                    yield return $"{q.Id}: ordering question repeats a step in its sequence.";
                    break;
            }
        }
    }

    public int CountFor(string domainKey) => All.Count(q => q.Domain == domainKey);

    public IEnumerable<string> ObjectivesFor(string domainKey) =>
        All.Where(q => q.Domain == domainKey).Select(q => q.Objective).Distinct().Order();

    public IEnumerable<string> AllTags() =>
        All.SelectMany(q => q.Tags).Distinct().Order();

    /// <summary>
    /// Draws <paramref name="count"/> questions from the given domains.
    /// When <paramref name="focusWeak"/> is set, questions you have never seen or have
    /// recently missed are strongly favoured; otherwise the draw is uniformly random.
    /// </summary>
    public List<Question> Draw(
        IReadOnlyCollection<string> domainKeys,
        int count,
        bool focusWeak,
        IReadOnlyDictionary<string, QuestionStat>? stats = null)
    {
        var pool = All.Where(q => domainKeys.Count == 0 || domainKeys.Contains(q.Domain)).ToList();
        return Present(Pick(pool, count, focusWeak, stats));
    }

    /// <summary>
    /// Builds a full exam set: <paramref name="count"/> questions split across the four
    /// domains in proportion to the published weights. Falls back to filling from the
    /// remaining pool if a domain does not have enough questions to cover its share.
    /// </summary>
    public List<Question> DrawExam(
        int count,
        bool focusWeak = false,
        IReadOnlyDictionary<string, QuestionStat>? stats = null)
    {
        var allocation = ExamDomains.Allocate(count);
        var chosen = new List<Question>();

        foreach (var (domainKey, want) in allocation)
        {
            var pool = All.Where(q => q.Domain == domainKey).ToList();
            chosen.AddRange(Pick(pool, want, focusWeak, stats));
        }

        if (chosen.Count < count)
        {
            var taken = chosen.Select(q => q.Id).ToHashSet();
            var filler = All.Where(q => !taken.Contains(q.Id)).ToList();
            chosen.AddRange(Pick(filler, count - chosen.Count, focusWeak, stats));
        }

        return Present(Shuffle(chosen));
    }

    /// <summary>
    /// Final step before questions leave the bank: hand back copies with the options in a
    /// random order. Every draw goes through here so no path can serve them in the authored
    /// order, where the correct answer sits at A far too often.
    /// </summary>
    private static List<Question> Present(List<Question> questions) =>
        [.. questions.Select(q => q.WithShuffledOptions(Random.Shared))];

    private static List<Question> Pick(
        List<Question> pool,
        int count,
        bool focusWeak,
        IReadOnlyDictionary<string, QuestionStat>? stats)
    {
        if (count <= 0 || pool.Count == 0) return [];
        if (pool.Count <= count) return Shuffle(pool);

        if (!focusWeak || stats is null)
        {
            return Shuffle(pool).Take(count).ToList();
        }

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
