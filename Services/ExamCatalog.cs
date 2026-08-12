using System.Text.Json;
using System.Text.Json.Serialization;
using Ai200Trainer.Models;

namespace Ai200Trainer.Services;

/// <summary>Everything loaded for one exam: its definition, questions and scenarios.</summary>
public sealed class ExamBank
{
    public required ExamDefinition Definition { get; init; }
    public required IReadOnlyList<Question> Questions { get; init; }
    public required IReadOnlyDictionary<string, CaseStudy> CaseStudies { get; init; }
    public required IReadOnlyList<string> Problems { get; init; }

    /// <summary>Raw cram sheet source, in the small markdown subset Cram.razor renders.</summary>
    public string? CramSheet { get; init; }

    public int CountFor(string domainKey) => Questions.Count(q => q.Domain == domainKey);

    public IEnumerable<string> ObjectivesFor(string domainKey) =>
        Questions.Where(q => q.Domain == domainKey).Select(q => q.Objective).Distinct().Order();

    public CaseStudy? CaseStudyFor(Question question) =>
        question.CaseStudyId is { } id && CaseStudies.TryGetValue(id, out var cs) ? cs : null;
}

/// <summary>
/// Loads every exam under <c>Data/</c>. Each subfolder is one exam: an <c>exam.json</c>
/// manifest, any number of question files, an optional <c>case-studies.json</c> and an
/// optional <c>cram.md</c>.
/// <para>
/// Singleton and read-only at runtime, so one instance serves every visitor. Adding a new
/// certification is a data change — drop in a folder — rather than a code change.
/// </para>
/// </summary>
public sealed class ExamCatalog(IWebHostEnvironment env, ILogger<ExamCatalog> logger)
{
    private const string ManifestFile = "exam.json";
    private const string CaseStudyFile = "case-studies.json";
    private const string CramFile = "cram.md";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly Lock _gate = new();
    private List<ExamBank> _exams = [];

    public string DataDirectory => Path.Combine(env.ContentRootPath, "Data");

    public DateTimeOffset LoadedAt { get; private set; }

    /// <summary>All exams, in the order their manifests sort by code.</summary>
    public IReadOnlyList<ExamBank> Exams
    {
        get { lock (_gate) return _exams; }
    }

    public ExamBank? Find(string? slug) =>
        slug is null ? null : Exams.FirstOrDefault(e => e.Definition.Slug == slug);

    /// <summary>The exam used when nothing has been chosen yet.</summary>
    public ExamBank? Default => Exams.FirstOrDefault();

    public void Load()
    {
        var loaded = new List<ExamBank>();

        if (!Directory.Exists(DataDirectory))
        {
            logger.LogError("No Data directory at {Path}.", DataDirectory);
        }
        else
        {
            foreach (var folder in Directory.EnumerateDirectories(DataDirectory).Order())
            {
                var bank = LoadExam(folder);
                if (bank is not null) loaded.Add(bank);
            }
        }

        loaded = [.. loaded.OrderBy(e => e.Definition.Code, StringComparer.OrdinalIgnoreCase)];

        lock (_gate) _exams = loaded;
        LoadedAt = DateTimeOffset.Now;

        foreach (var e in loaded)
        {
            logger.LogInformation(
                "Loaded {Code}: {Questions} questions, {Studies} case study/studies, {Problems} problem(s).",
                e.Definition.Code, e.Questions.Count, e.CaseStudies.Count, e.Problems.Count);
        }
    }

    private ExamBank? LoadExam(string folder)
    {
        var manifestPath = Path.Combine(folder, ManifestFile);
        if (!File.Exists(manifestPath))
        {
            logger.LogWarning("Skipping {Folder}: no {Manifest}.", folder, ManifestFile);
            return null;
        }

        ExamDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<ExamDefinition>(File.ReadAllText(manifestPath), JsonOptions)
                         ?? throw new JsonException("manifest parsed to null");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Could not parse {Path}.", manifestPath);
            return null;
        }

        if (string.IsNullOrWhiteSpace(definition.Slug))
        {
            definition.Slug = Path.GetFileName(folder);
        }

        var questions = new List<Question>();
        var caseStudies = new Dictionary<string, CaseStudy>();
        var problems = new List<string>();

        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order())
        {
            var name = Path.GetFileName(path);
            if (name.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                if (name.Equals(CaseStudyFile, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var study in JsonSerializer.Deserialize<List<CaseStudy>>(File.ReadAllText(path), JsonOptions) ?? [])
                    {
                        caseStudies[study.Id] = study;
                    }
                    continue;
                }

                var parsed = JsonSerializer.Deserialize<List<Question>>(File.ReadAllText(path), JsonOptions);
                if (parsed is null) { problems.Add($"{name}: file parsed to null."); continue; }
                questions.AddRange(parsed);
            }
            catch (JsonException ex)
            {
                problems.Add($"{name}: {ex.Message}");
                logger.LogError(ex, "Failed to parse {File}", path);
            }
        }

        // Blanks questions carry their answers on the blanks themselves; mirror them into
        // Correct so downstream code (counts, labels) does not need to special-case them.
        foreach (var q in questions.Where(q => q.Kind == QuestionKind.Blanks))
        {
            q.Correct = [.. q.Blanks.Select(b => Question.BlankToken(b.Id, b.Correct))];
        }

        problems.AddRange(Validate(definition, questions, caseStudies));

        var cramPath = Path.Combine(folder, CramFile);
        var cram = File.Exists(cramPath) ? File.ReadAllText(cramPath) : null;

        return new ExamBank
        {
            Definition = definition,
            Questions = questions,
            CaseStudies = caseStudies,
            Problems = problems,
            CramSheet = cram
        };
    }

    private static IEnumerable<string> Validate(
        ExamDefinition definition,
        List<Question> questions,
        Dictionary<string, CaseStudy> caseStudies)
    {
        if (definition.Domains.Count == 0)
        {
            yield return $"{definition.Code}: the manifest declares no domains.";
        }

        foreach (var duplicate in questions.GroupBy(q => q.Id).Where(g => g.Count() > 1))
        {
            yield return $"Duplicate question id '{duplicate.Key}' appears {duplicate.Count()} times.";
        }

        var domainKeys = definition.Domains.Select(d => d.Key).ToHashSet();

        foreach (var q in questions)
        {
            if (!domainKeys.Contains(q.Domain))
            {
                yield return $"{q.Id}: unknown domain '{q.Domain}' for {definition.Code}.";
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
                if (q.Blanks.Count == 0) yield return $"{q.Id}: a blanks question needs at least one blank.";

                foreach (var duplicate in q.Blanks.GroupBy(b => b.Id).Where(g => g.Count() > 1))
                {
                    yield return $"{q.Id}: duplicate blank id '{duplicate.Key}'.";
                }

                foreach (var b in q.Blanks)
                {
                    if (b.Choices.Count < 2) yield return $"{q.Id}/{b.Id}: needs at least two choices.";
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

            if (q.Options.Count < 2) yield return $"{q.Id}: needs at least two options.";

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

                case QuestionKind.Ordering when q.Correct.Count != q.Options.Count:
                    yield return $"{q.Id}: ordering question lists {q.Correct.Count} steps but has {q.Options.Count} options.";
                    break;

                case QuestionKind.Ordering when q.Correct.Distinct().Count() != q.Correct.Count:
                    yield return $"{q.Id}: ordering question repeats a step in its sequence.";
                    break;
            }
        }
    }
}
