using System.Text.Json.Serialization;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Learning.Domain;
using LanguZe.Api.Platform.OpenApi;

namespace LanguZe.Api.Modules.Learning.Shared;

/// <summary>A session and where the learner has got to in it, field for field as NestJS's <c>SessionDto</c> (FR-031, FR-036).</summary>
/// <param name="WorldId">The world being played; empty for a review.</param>
/// <param name="NextQuestion">What to ask next; empty when nothing is left to ask.</param>
/// <param name="Summary">Only for a COMPLETED session. One left unfinished has none (FR-036).</param>
internal sealed record SessionResponse(
    Guid Id,
    SessionKindName Kind,
    SessionStatusName Status,
    [property: ApiPropertyOptional] Guid? WorldId,
    int AnsweredCount,
    int QuestionCount,
    [property: ApiPropertyOptional] QuestionResponse? NextQuestion,
    DateTimeOffset StartedAt,
    [property: ApiPropertyOptional] SessionSummaryResponse? Summary);

/// <summary>
/// One question as the learner sees it: the photo and the box around the object, and nothing
/// else. The word, its meaning and its variants are never here (FR-032, S5).
/// </summary>
/// <param name="PhotoUrl">A signed link to the world's photo (P4).</param>
/// <param name="Arrow">
/// Points at the asked object when its box holds others too (#73); empty when there is no safe
/// arrow, as for worlds made before it, and the box alone shows the object.
/// </param>
internal sealed record QuestionResponse(Guid Id, int Position, [property: ApiPropertyOptional] string? PhotoUrl, QuestionBoxResponse Box, [property: ApiPropertyOptional] QuestionArrowResponse? Arrow);

/// <summary>Where the object is, as fractions of the photo (FR-031).</summary>
internal sealed record QuestionBoxResponse(double X, double Y, double Width, double Height);

/// <summary>An arrow from <paramref name="Tail"/>, where the question's number sits, to <paramref name="Point"/>, on the object (#73).</summary>
internal sealed record QuestionArrowResponse(PhotoPointResponse Point, PhotoPointResponse Tail);

/// <summary>A spot on the photo, as fractions of its width and height.</summary>
internal sealed record PhotoPointResponse(double X, double Y);

/// <summary>What the learner achieved in a session (FR-035, US-033).</summary>
internal sealed record SessionSummaryResponse(int AnsweredCount, int CorrectCount, int XpEarned, IReadOnlyList<LevelChangeResponse> LevelChanges);

/// <summary>A word that moved during a session, and where it ended (FR-035).</summary>
internal sealed record LevelChangeResponse(string English, LevelName Level);

/// <summary>The feedback one answer earns, field for field as NestJS's <c>AnswerResultDto</c> (FR-033).</summary>
/// <param name="AlreadyAnswered">This question was already answered; nothing was recorded again (FR-034).</param>
/// <param name="Answer">What the learner typed, as recorded; empty for "I don't know" (FR-033).</param>
/// <param name="MatchedVariant">Right because it matched one of the word's accepted variants, not the word itself (FR-033).</param>
internal sealed record AnswerResultResponse(
    bool Correct,
    bool DontKnow,
    bool AlreadyAnswered,
    [property: ApiPropertyOptional] string? Answer,
    bool MatchedVariant,
    WordFeedbackResponse Word,
    int XpAwarded,
    MasteryChangeResponse Mastery,
    bool SessionCompleted,
    [property: ApiPropertyOptional] SessionSummaryResponse? Summary);

/// <summary>The word a question was about, shown only once it has been answered (FR-033).</summary>
internal sealed record WordFeedbackResponse(string English, string ThaiMeaning, string ExampleSentence);

/// <summary>Where an answer left the word (SRS 4.1). <c>before</c> is empty when it was new.</summary>
internal sealed record MasteryChangeResponse([property: ApiPropertyOptional] LevelName? Before, LevelName After);

internal enum SessionKindName
{
    [JsonStringEnumMemberName("GAME")]
    Game,

    [JsonStringEnumMemberName("REVIEW")]
    Review,
}

internal enum SessionStatusName
{
    [JsonStringEnumMemberName("IN_PROGRESS")]
    InProgress,

    [JsonStringEnumMemberName("COMPLETED")]
    Completed,

    [JsonStringEnumMemberName("ABANDONED")]
    Abandoned,
}

internal enum LevelName
{
    [JsonStringEnumMemberName("LEARNING")]
    Learning,

    [JsonStringEnumMemberName("FAMILIAR")]
    Familiar,

    [JsonStringEnumMemberName("MASTERED")]
    Mastered,
}

/// <summary>A word's level between the database, the rules and the answer.</summary>
internal static class Levels
{
    public static Stage? StageOf(MasteryLevel? level) => level switch
    {
        MasteryLevel.Learning => Stage.Learning,
        MasteryLevel.Familiar => Stage.Familiar,
        MasteryLevel.Mastered => Stage.Mastered,
        _ => null,
    };

    public static MasteryLevel LevelOf(Stage stage) => stage switch
    {
        Stage.Familiar => MasteryLevel.Familiar,
        Stage.Mastered => MasteryLevel.Mastered,
        _ => MasteryLevel.Learning,
    };

    public static LevelName NameOf(Stage stage) => stage switch
    {
        Stage.Familiar => LevelName.Familiar,
        Stage.Mastered => LevelName.Mastered,
        _ => LevelName.Learning,
    };

    public static SessionSummaryResponse ResponseOf(SessionSummary summary) => new(
        summary.AnsweredCount,
        summary.CorrectCount,
        summary.XpEarned,
        [.. summary.LevelChanges.Select(change => new LevelChangeResponse(change.English, NameOf(change.Level)))]);
}
