namespace Ai200Trainer.Models;

public enum QuestionKind
{
    /// <summary>Exactly one correct option (radio buttons).</summary>
    Single,

    /// <summary>Two or more correct options (checkboxes). The stem states how many.</summary>
    Multi
}

public sealed class Option
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class DocLink
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class Question
{
    public string Id { get; set; } = "";

    /// <summary>Key into <see cref="ExamDomains"/> — containers, data, integration, operations.</summary>
    public string Domain { get; set; } = "";

    /// <summary>The sub-heading from the official skills outline, e.g. "Implement container application hosting".</summary>
    public string Objective { get; set; } = "";

    public QuestionKind Kind { get; set; } = QuestionKind.Single;

    /// <summary>1 = recall, 2 = applied, 3 = tricky / multi-step reasoning.</summary>
    public int Difficulty { get; set; } = 2;

    public string Stem { get; set; } = "";

    /// <summary>Optional code, CLI, YAML or KQL block rendered above the options.</summary>
    public string? Code { get; set; }

    public List<Option> Options { get; set; } = [];

    public List<string> Correct { get; set; } = [];

    public string Explanation { get; set; } = "";

    /// <summary>Option id → why that distractor is wrong. Optional, shown under the explanation.</summary>
    public Dictionary<string, string> WhyWrong { get; set; } = [];

    public List<DocLink> Docs { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public bool IsCorrect(IReadOnlyCollection<string> selected) =>
        selected.Count == Correct.Count && Correct.All(selected.Contains);

    public string CorrectLabel => string.Join(", ", Correct.Order());
}
