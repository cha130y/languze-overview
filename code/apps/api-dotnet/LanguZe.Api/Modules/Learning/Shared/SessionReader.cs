using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Learning.Domain;
using LanguZe.Api.Modules.Storage.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Learning.Shared;

/// <summary>
/// Where a learner is in a session, and when one is over (sessions.service.ts, <c>toDto</c> and
/// <c>closeIfStale</c>; FR-031, FR-036, V17). Starting and answering write; this only reads,
/// apart from closing a session left for a day.
/// </summary>
internal sealed class SessionReader(LanguZeDbContext db, IPhotoStorage storage, TimeProvider clock)
{
    /// <summary>How long an unfinished session stays open (V17, FR-036).</summary>
    public static readonly TimeSpan OpenFor = TimeSpan.FromHours(24);

    /// <summary>Whether a session still in progress has been left for a day, whatever its row says (V17).</summary>
    public bool IsStale(PracticeSession session) =>
        session.Status == SessionStatus.InProgress && clock.GetUtcNow() - session.StartedAt >= OpenFor;

    /// <summary>
    /// Closes a session left for a day, when someone looks at it rather than by a scheduled task:
    /// nothing depends on the row changing before then. Answers whether it closed it.
    /// </summary>
    public async Task<bool> CloseIfStaleAsync(PracticeSession session, CancellationToken cancellationToken)
    {
        if (!IsStale(session))
        {
            return false;
        }

        await AbandonAsync(session.Id, cancellationToken);
        return true;
    }

    /// <summary>Marks a session still in progress as abandoned; one that finished another way is left alone.</summary>
    public Task AbandonAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.PracticeSessions.Where(session => session.Id == sessionId && session.Status == SessionStatus.InProgress)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.Status, SessionStatus.Abandoned), cancellationToken);

    /// <summary>The session as the learner sees it, as <paramref name="session"/> holds it.</summary>
    public async Task<SessionResponse> ResponseOfAsync(PracticeSession session, CancellationToken cancellationToken)
    {
        var questions = await (
                from question in db.SessionQuestions
                where question.SessionId == session.Id
                join attempt in db.Attempts on (Guid?)question.Id equals attempt.QuestionId into attempts
                from attempt in attempts.DefaultIfEmpty()
                join word in db.VocabularyWords on question.VocabularyWordId equals (Guid?)word.Id into words
                from word in words.DefaultIfEmpty()
                join occurrence in db.WordOccurrences on question.OccurrenceId equals (Guid?)occurrence.Id into occurrences
                from occurrence in occurrences.DefaultIfEmpty()
                orderby question.Position
                select new { question.Id, question.Position, Attempt = attempt, English = word == null ? null : word.English, Occurrence = occurrence })
            .ToListAsync(cancellationToken);

        // A question whose word was removed since the session started is skipped (UC-030 3a).
        var next = questions.FirstOrDefault(question => question.Attempt is null && question.Occurrence is not null);
        QuestionResponse? nextQuestion = null;
        if (next is not null)
        {
            var occurrence = next.Occurrence!;
            var photo = await db.Worlds.Where(world => world.Id == occurrence.WorldId)
                .Select(world => world.Photo == null ? null : world.Photo.StorageKey)
                .SingleOrDefaultAsync(cancellationToken);
            nextQuestion = new QuestionResponse(
                next.Id,
                next.Position,
                photo is null ? null : await storage.SignedLinkAsync(photo),
                new QuestionBoxResponse(occurrence.BoxX, occurrence.BoxY, occurrence.BoxWidth, occurrence.BoxHeight),
                occurrence is { PointX: { } pointX, PointY: { } pointY, TailX: { } tailX, TailY: { } tailY }
                    ? new QuestionArrowResponse(new PhotoPointResponse(pointX, pointY), new PhotoPointResponse(tailX, tailY))
                    : null);
        }

        // Only a session that ran to its end has a summary (FR-036).
        var summary = session.Status == SessionStatus.Completed
            ? Levels.ResponseOf(SessionSummaries.Of([
                .. questions
                    .Where(question => question.Attempt is not null && question.English is not null)
                    .Select(question => SummarisedOf(question.Attempt!, question.English!)),
            ]))
            : null;

        return new SessionResponse(
            session.Id,
            session.Kind == SessionKind.Review ? SessionKindName.Review : SessionKindName.Game,
            session.Status switch
            {
                SessionStatus.Completed => SessionStatusName.Completed,
                SessionStatus.Abandoned => SessionStatusName.Abandoned,
                _ => SessionStatusName.InProgress,
            },
            session.WorldId,
            questions.Count(question => question.Attempt is not null),
            questions.Count,
            nextQuestion,
            session.StartedAt,
            summary);
    }

    /// <summary>What a session achieved, from its attempts in the order they were answered (FR-035).</summary>
    public async Task<SessionSummaryResponse> SummaryOfAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var attempts = await (
                from attempt in db.Attempts
                where attempt.SessionId == sessionId
                join word in db.VocabularyWords on attempt.VocabularyWordId equals word.Id
                orderby attempt.AnsweredAt
                select new { Attempt = attempt, word.English })
            .ToListAsync(cancellationToken);

        return Levels.ResponseOf(SessionSummaries.Of([.. attempts.Select(row => SummarisedOf(row.Attempt, row.English))]));
    }

    private static SummarisedAttempt SummarisedOf(Attempt attempt, string english) => new(
        attempt.IsCorrect,
        attempt.XpAwarded,
        Levels.StageOf(attempt.LevelBefore),
        Levels.StageOf(attempt.LevelAfter)!.Value,
        english);
}
