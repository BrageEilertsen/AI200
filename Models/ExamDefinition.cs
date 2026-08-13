namespace Ai200Trainer.Models;

/// <summary>
/// One skill area from an exam's published outline. Weights are the published range;
/// <see cref="Weight"/> is the midpoint, which is what exam simulation uses to decide how
/// many questions to draw from each area.
/// </summary>
public sealed class ExamDomain
{
    /// <summary>Stable key used by questions to declare which area they belong to.</summary>
    public string Key { get; set; } = "";

    /// <summary>The area's full name, as published.</summary>
    public string Name { get; set; } = "";

    /// <summary>Short label for tight spaces such as chips and table columns.</summary>
    public string ShortName { get; set; } = "";

    public int MinWeight { get; set; }
    public int MaxWeight { get; set; }

    /// <summary>A CSS colour, usually a custom property such as <c>var(--d-1)</c>.</summary>
    public string Accent { get; set; } = "var(--text-3)";

    public double Weight => (MinWeight + MaxWeight) / 2.0;

    public string WeightLabel => $"{MinWeight}–{MaxWeight}%";
}

/// <summary>
/// Everything that makes one certification exam different from another: its identity, its
/// skill areas and weights, and the shape of a realistic mock.
/// <para>
/// Loaded from <c>Data/&lt;slug&gt;/exam.json</c> so adding an exam is a data change rather
/// than a code change. Domain count, weights and pass mark all vary between exams — AI-200
/// has four evenly weighted areas, AZ-400 has five of which one is over half the paper.
/// </para>
/// </summary>
/// <summary>
/// One learning path from the certification's published course syllabus. Skill areas say how
/// the exam is weighted; learning paths say how Microsoft teaches it, which is the more useful
/// grouping while you are still working through the material.
/// </summary>
public sealed class LearningPath
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>Module count as the syllabus lists it.</summary>
    public int Modules { get; set; }

    /// <summary>The Azure service the path is built around, e.g. "Azure Container Registry".</summary>
    public string Product { get; set; } = "";

    /// <summary>Key of the skill area this path sits under, used for its accent colour.</summary>
    public string Domain { get; set; } = "";
}

public sealed class ExamDefinition
{
    /// <summary>Folder name and URL segment, e.g. <c>ai-200</c>.</summary>
    public string Slug { get; set; } = "";

    /// <summary>Exam code as Microsoft writes it, e.g. <c>AI-200</c>.</summary>
    public string Code { get; set; } = "";

    /// <summary>Exam title, e.g. "Developing AI Cloud Solutions on Azure".</summary>
    public string Title { get; set; } = "";

    /// <summary>Certification earned, e.g. "Azure AI Cloud Developer Associate".</summary>
    public string Certification { get; set; } = "";

    /// <summary>One or two sentences shown under the dashboard heading.</summary>
    public string Blurb { get; set; } = "";

    public int MinQuestions { get; set; } = 40;
    public int MaxQuestions { get; set; } = 60;
    public int MinMinutes { get; set; } = 100;
    public int MaxMinutes { get; set; } = 120;

    /// <summary>Scaled score needed to pass, out of 1000.</summary>
    public int PassMark { get; set; } = 700;

    /// <summary>Question counts offered on the exam simulation screen.</summary>
    public List<int> MockSizes { get; set; } = [40, 50, 60];

    /// <summary>Time limits offered, aligned by index with <see cref="MockSizes"/>.</summary>
    public List<int> MockMinutes { get; set; } = [90, 110, 120];

    public List<ExamDomain> Domains { get; set; } = [];

    /// <summary>Link to the published skills outline.</summary>
    public string? StudyGuideUrl { get; set; }

    /// <summary>Link to the certification page carrying the course syllabus.</summary>
    public string? SyllabusUrl { get; set; }

    /// <summary>Difficulty as the syllabus labels it, e.g. "Intermediate" or "Advanced".</summary>
    public string? SyllabusLevel { get; set; }

    /// <summary>Audience as the syllabus labels it, e.g. "Developer" or "Administrator".</summary>
    public string? SyllabusRole { get; set; }

    /// <summary>
    /// The published course syllabus, in the order Microsoft lists it. Optional: an exam with
    /// none falls back to grouping the bank by skill area.
    /// </summary>
    public List<LearningPath> LearningPaths { get; set; } = [];

    public bool HasSyllabus => LearningPaths.Count > 0;

    public LearningPath? Path(string? key) =>
        key is null ? null : LearningPaths.FirstOrDefault(p => p.Key == key);

    public int DefaultMockSize => MockSizes.Count > 1 ? MockSizes[1] : MockSizes.FirstOrDefault(50);

    public int DefaultMockMinutes => MockMinutes.Count > 1 ? MockMinutes[1] : MockMinutes.FirstOrDefault(110);

    public string QuestionRangeLabel => $"{MinQuestions}–{MaxQuestions} questions";

    public string DurationLabel => $"{MinMinutes}–{MaxMinutes} minutes";

    public ExamDomain Domain(string key) =>
        Domains.FirstOrDefault(d => d.Key == key)
        ?? new ExamDomain { Key = key, Name = key, ShortName = key };

    /// <summary>
    /// Splits <paramref name="total"/> questions across the domains in proportion to the
    /// published weights, handing any rounding remainder to the heaviest domains first.
    /// </summary>
    public Dictionary<string, int> Allocate(int total)
    {
        if (Domains.Count == 0) return [];

        var sum = Domains.Sum(d => d.Weight);
        if (sum <= 0) return Domains.ToDictionary(d => d.Key, _ => total / Domains.Count);

        var exact = Domains.ToDictionary(d => d.Key, d => d.Weight / sum * total);
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
