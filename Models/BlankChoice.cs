namespace Ai200Trainer.Models;

/// <summary>
/// One dropdown selection travelling from the question view back to the session.
/// A null <paramref name="ChoiceId"/> means the blank was cleared.
/// </summary>
public sealed record BlankChoice(string BlankId, string? ChoiceId);
