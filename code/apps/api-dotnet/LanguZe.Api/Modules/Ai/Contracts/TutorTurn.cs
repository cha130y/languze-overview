using System.Text.Json;
using LanguZe.Api.Data.Entities;

namespace LanguZe.Api.Modules.Ai.Contracts;

/// <summary>One parameter a tutor tool takes. Two shapes, <c>integer</c> and <c>string</c>, are all three tools need.</summary>
public sealed record ToolParameter(string Name, string Type, string Description);

/// <summary>
/// Something the tutor may look up (ai-provider.ts, <c>TutorTool</c>; FR-072). LanguZe builds
/// these already bound to the signed-in learner, so no tool takes a learner identifier and the
/// model has no way to name one. Tools only read. Input from the model is untrusted, so each
/// tool checks it and answers a bad one with an explanation rather than with data.
/// </summary>
public sealed record TutorTool(
    string Name,
    string Description,
    IReadOnlyList<ToolParameter> Parameters,
    Func<IReadOnlyDictionary<string, JsonElement>, CancellationToken, Task<object>> Run);

/// <summary>What the learner and the tutor have said so far, oldest first (FR-070).</summary>
public sealed record TutorExchange(TutorRole Role, string Content);

/// <summary>One turn of the conversation, and what the tutor may look up during it.</summary>
public sealed record TutorTurn(IReadOnlyList<TutorExchange> History, string Message, IReadOnlyList<TutorTool> Tools);

/// <summary>
/// The reply as it arrives (NFR-003): pieces of text, then the end of the turn with what it
/// cost (AIR-007).
/// </summary>
public abstract record TutorEvent;

/// <summary>A piece of the reply.</summary>
public sealed record TutorDelta(string Text) : TutorEvent;

/// <summary>The end of the turn, with its cost when the provider reports one.</summary>
public sealed record TutorDone(AiUsage? Usage) : TutorEvent;
