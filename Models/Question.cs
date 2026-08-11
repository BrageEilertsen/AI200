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

    /// <summary>
    /// Returns a copy with the options in a random order, relabelled A, B, C… so the letters
    /// still read top to bottom. Correct answers and per-distractor notes are remapped to the
    /// new labels.
    /// <para>
    /// This exists because the bank was authored with the correct answer written first, which
    /// left it in position A for 92% of single-answer questions — you could score 92% by always
    /// picking A. Shuffling at presentation time fixes that regardless of how the JSON is
    /// ordered, and stops repeated practice teaching the position rather than the answer.
    /// </para>
    /// </summary>
    public Question WithShuffledOptions(Random rng)
    {
        var order = Options.ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var relabel = new Dictionary<string, string>(order.Count);
        var options = new List<Option>(order.Count);

        for (var i = 0; i < order.Count; i++)
        {
            var label = ((char)('A' + i)).ToString();
            relabel[order[i].Id] = label;
            options.Add(new Option { Id = label, Text = order[i].Text });
        }

        return new Question
        {
            Id = Id,
            Domain = Domain,
            Objective = Objective,
            Kind = Kind,
            Difficulty = Difficulty,
            Stem = Stem,
            Code = Code,
            Options = options,
            Correct = [.. Correct.Where(relabel.ContainsKey).Select(c => relabel[c]).Order()],
            Explanation = Explanation,
            WhyWrong = WhyWrong
                .Where(kv => relabel.ContainsKey(kv.Key))
                .ToDictionary(kv => relabel[kv.Key], kv => kv.Value),
            Docs = Docs,
            Tags = Tags
        };
    }
}
