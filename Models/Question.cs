namespace Ai200Trainer.Models;

public enum QuestionKind
{
    /// <summary>Exactly one correct option (radio buttons).</summary>
    Single,

    /// <summary>Two or more correct options (checkboxes). The stem states how many.</summary>
    Multi,

    /// <summary>
    /// Build-list: put the steps in the correct sequence. The answer is order-sensitive,
    /// so <see cref="Question.Correct"/> is a sequence rather than a set.
    /// </summary>
    Ordering,

    /// <summary>
    /// One choice per blank. Covers both "complete the code" dropdowns and yes/no
    /// statement series — a yes/no series is just a set of blanks whose choices are
    /// Yes and No.
    /// </summary>
    Blanks
}

public sealed class Option
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>One dropdown in a <see cref="QuestionKind.Blanks"/> question.</summary>
public sealed class Blank
{
    public string Id { get; set; } = "";

    /// <summary>The statement being judged, or the caption for the gap being filled.</summary>
    public string Label { get; set; } = "";

    public List<Option> Choices { get; set; } = [];

    /// <summary>Id of the correct choice.</summary>
    public string Correct { get; set; } = "";
}

public sealed class DocLink
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>
/// A shared scenario that several questions hang off, mirroring the case-study sections of
/// the real exam. Stored separately so the text is written once.
/// </summary>
public sealed class CaseStudy
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>The scenario: requirements, current architecture, constraints.</summary>
    public string Body { get; set; } = "";
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

    /// <summary>
    /// A nudge shown on request before answering — the discriminating idea, never the answer.
    /// Written by hand rather than generated at run time: a hint that is confidently wrong is
    /// worse than no hint, and stored text can be reviewed once instead of trusted every time.
    /// Optional; the button only appears where one exists.
    /// </summary>
    public string? Hint { get; set; }

    /// <summary>Id of a shared scenario in <c>case-studies.json</c>, when this is a case-study question.</summary>
    public string? CaseStudyId { get; set; }

    /// <summary>Choices for Single, Multi and Ordering. Unused by Blanks.</summary>
    public List<Option> Options { get; set; } = [];

    /// <summary>Dropdowns for Blanks. Unused by the other kinds.</summary>
    public List<Blank> Blanks { get; set; } = [];

    /// <summary>
    /// Option ids. A set for Single and Multi; an ordered sequence for Ordering.
    /// Derived from <see cref="Blanks"/> for Blanks questions, so it is not set in JSON.
    /// </summary>
    public List<string> Correct { get; set; } = [];

    public string Explanation { get; set; } = "";

    /// <summary>Option id → why that distractor is wrong. Optional, shown under the explanation.</summary>
    public Dictionary<string, string> WhyWrong { get; set; } = [];

    public List<DocLink> Docs { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    /// <summary>Encodes one blank's answer for storage in the flat selection list.</summary>
    public static string BlankToken(string blankId, string choiceId) => $"{blankId}={choiceId}";

    public bool IsCorrect(IReadOnlyCollection<string> selected) => Kind switch
    {
        // Order matters: the whole point is getting the sequence right.
        QuestionKind.Ordering => selected.SequenceEqual(Correct),

        // Every blank must be answered, and answered correctly.
        QuestionKind.Blanks => Blanks.Count > 0
            && Blanks.All(b => selected.Contains(BlankToken(b.Id, b.Correct))),

        _ => selected.Count == Correct.Count && Correct.All(selected.Contains)
    };

    public string CorrectLabel => Kind switch
    {
        QuestionKind.Ordering => string.Join(" → ", Correct),
        QuestionKind.Blanks => string.Join(", ", Blanks.Select((b, i) => $"{i + 1}: {ChoiceText(b, b.Correct)}")),
        _ => string.Join(", ", Correct.Order())
    };

    /// <summary>
    /// Choice text for use in a plain-text label. Backticks are stripped because this ends up
    /// in the verdict line, where there is no opportunity to render a code span.
    /// </summary>
    private static string ChoiceText(Blank blank, string choiceId) =>
        (blank.Choices.FirstOrDefault(c => c.Id == choiceId)?.Text ?? choiceId).Replace("`", string.Empty);

    /// <summary>How many selections a complete answer needs. Drives the "answered" check.</summary>
    public int RequiredSelections => Kind switch
    {
        QuestionKind.Ordering => Options.Count,
        QuestionKind.Blanks => Blanks.Count,
        QuestionKind.Multi => Correct.Count,
        _ => 1
    };

    /// <summary>
    /// Returns a copy with the answer choices in a random order, relabelled A, B, C… so the
    /// letters still read top to bottom.
    /// <para>
    /// This exists because the bank was authored with the correct answer written first, which
    /// left it in position A for 92% of single-answer questions — you could score 92% by always
    /// picking A. Shuffling at presentation time fixes that regardless of how the JSON is
    /// ordered, and stops repeated practice teaching the position rather than the answer.
    /// </para>
    /// For Ordering the presented list is shuffled but the correct sequence is preserved, and
    /// for Blanks each dropdown's choices are shuffled independently.
    /// </summary>
    public Question WithShuffledOptions(Random rng)
    {
        var copy = new Question
        {
            Id = Id,
            Domain = Domain,
            Objective = Objective,
            Kind = Kind,
            Difficulty = Difficulty,
            Stem = Stem,
            Code = Code,
            Hint = Hint,
            CaseStudyId = CaseStudyId,
            Explanation = Explanation,
            Docs = Docs,
            Tags = Tags
        };

        if (Kind == QuestionKind.Blanks)
        {
            copy.Blanks = [.. Blanks.Select(b =>
            {
                var (choices, relabel) = Relabel(Shuffle(b.Choices, rng));
                return new Blank
                {
                    Id = b.Id,
                    Label = b.Label,
                    Choices = choices,
                    Correct = relabel.GetValueOrDefault(b.Correct, b.Correct)
                };
            })];
            return copy;
        }

        var (options, map) = Relabel(Shuffle(Options, rng));
        copy.Options = options;

        // Ordering answers are a sequence, so map in place rather than sorting.
        copy.Correct = Kind == QuestionKind.Ordering
            ? [.. Correct.Where(map.ContainsKey).Select(c => map[c])]
            : [.. Correct.Where(map.ContainsKey).Select(c => map[c]).Order()];

        copy.WhyWrong = WhyWrong
            .Where(kv => map.ContainsKey(kv.Key))
            .ToDictionary(kv => map[kv.Key], kv => kv.Value);

        return copy;
    }

    private static List<Option> Shuffle(List<Option> source, Random rng)
    {
        var list = source.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>Relabels options A, B, C… in their new order and returns the old-id → new-id map.</summary>
    private static (List<Option> Options, Dictionary<string, string> Map) Relabel(List<Option> ordered)
    {
        var map = new Dictionary<string, string>(ordered.Count);
        var options = new List<Option>(ordered.Count);

        for (var i = 0; i < ordered.Count; i++)
        {
            var label = ((char)('A' + i)).ToString();
            map[ordered[i].Id] = label;
            options.Add(new Option { Id = label, Text = ordered[i].Text });
        }

        return (options, map);
    }
}
