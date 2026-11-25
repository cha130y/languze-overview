using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Learning.Domain;
using LanguZe.Api.Modules.Learning.Shared;
using LanguZe.Api.Platform.Errors;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Learning.Features.StartSession;

/// <summary>
/// A new game for a world, or a new review (sessions.service.ts, <c>start</c> and
/// <c>startReview</c>; FR-030, FR-050). The questions are chosen and written once, here, so the
/// session a learner comes back to tomorrow asks the same words in the same order as the one
/// they left.
/// </summary>
internal sealed class StartSessionHandler(LanguZeDbContext db, SessionReader reader, TimeProvider clock)
{
    public async Task<SessionResponse> HandleAsync(Guid learnerId, StartSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind == "REVIEW")
        {
            return await StartReviewAsync(learnerId, cancellationToken);
        }

        // The rules require a world for a game, so this is one by now.
        var worldId = Guid.Parse(request.WorldId!);
        var world = await db.Worlds
            .Include(row => row.Occurrences)
                .ThenInclude(occurrence => occurrence.VocabularyWord)
                    .ThenInclude(word => word.Mastery)
            .AsSplitQuery()
            .FirstOrDefaultAsync(row => row.Id == worldId && row.LearnerId == learnerId, cancellationToken)
            // A world that is not the learner's is missing, not forbidden (FR-008).
            ?? throw new AppException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "This world does not exist.");
        if (world.Status != WorldStatus.Ready || world.Occurrences.Count == 0)
        {
            throw new AppException(ErrorCodes.WorldNotReady, StatusCodes.Status409Conflict, "This world has no words to play with yet.");
        }

        var chosen = QuestionSelection.Select([
            .. world.Occurrences.Select(occurrence => new Candidate(occurrence.Id, occurrence.VocabularyWordId, Levels.StageOf(occurrence.VocabularyWord.Mastery?.Level))),
        ]);

        // To the millisecond, as the database keeps it, so the answer shows what a later read will.
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        var session = new PracticeSession
        {
            LearnerId = learnerId,
            Kind = SessionKind.Game,
            WorldId = worldId,
            Status = SessionStatus.InProgress,
            StartedAt = now,
            Questions = [.. chosen.Select((question, index) => new SessionQuestion { Position = (short)(index + 1), VocabularyWordId = question.VocabularyWordId, OccurrenceId = question.OccurrenceId })],
        };

        // Closing the old game and opening the new one together keeps a learner to one unfinished
        // game per world (FR-036); the database's partial unique index holds the rule when two race.
        return await OpenAsync(
            session,
            db.PracticeSessions.Where(row => row.LearnerId == learnerId && row.WorldId == worldId && row.Kind == SessionKind.Game && row.Status == SessionStatus.InProgress),
            cancellationToken);
    }

    /// <summary>
    /// A new review, drawn from every world the learner has (FR-050). It names no world: each
    /// question uses the word's occurrence in the learner's newest world that has it, the photo
    /// they saw it in last (FR-051).
    /// </summary>
    private async Task<SessionResponse> StartReviewAsync(Guid learnerId, CancellationToken cancellationToken)
    {
        // A MASTERED word has nothing to repair, and one never answered has no mastery at all.
        var rows = await db.WordMastery
            .Where(mastery => mastery.LearnerId == learnerId && (mastery.Level == MasteryLevel.Learning || mastery.Level == MasteryLevel.Familiar))
            .Select(mastery => new
            {
                mastery.VocabularyWordId,
                mastery.Level,
                mastery.LastPractisedAt,
                OccurrenceId = (
                    from occurrence in db.WordOccurrences
                    where occurrence.VocabularyWordId == mastery.VocabularyWordId
                    join world in db.Worlds on occurrence.WorldId equals world.Id
                    orderby world.CreatedAt descending
                    select (Guid?)occurrence.Id).FirstOrDefault(),
                LastMistakeAt = db.Attempts
                    .Where(attempt => attempt.VocabularyWordId == mastery.VocabularyWordId && !attempt.IsCorrect)
                    .Max(attempt => (DateTimeOffset?)attempt.AnsweredAt),
            })
            .ToListAsync(cancellationToken);

        var chosen = ReviewSelection.Select(
            [
                // A word whose last world went with it has no photo to show it in.
                .. rows.Where(row => row.OccurrenceId is not null)
                    .Select(row => new ReviewCandidate(row.VocabularyWordId, row.OccurrenceId!.Value, Levels.StageOf(row.Level)!.Value, row.LastPractisedAt, row.LastMistakeAt)),
            ],
            QuestionSelection.QuestionsPerSession);
        if (chosen.Count == 0)
        {
            throw new AppException(ErrorCodes.NothingToReview, StatusCodes.Status409Conflict, "There is nothing to review yet.");
        }

        var session = new PracticeSession
        {
            LearnerId = learnerId,
            Kind = SessionKind.Review,
            Status = SessionStatus.InProgress,
            StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds()),
            Questions = [.. chosen.Select((question, index) => new SessionQuestion { Position = (short)(index + 1), VocabularyWordId = question.VocabularyWordId, OccurrenceId = question.OccurrenceId })],
        };

        // One unfinished review at a time (FR-036), held by its own partial unique index.
        return await OpenAsync(
            session,
            db.PracticeSessions.Where(row => row.LearnerId == learnerId && row.Kind == SessionKind.Review && row.Status == SessionStatus.InProgress),
            cancellationToken);
    }

    /// <summary>Abandons the sessions <paramref name="replaced"/> finds and opens <paramref name="session"/>, together.</summary>
    private async Task<SessionResponse> OpenAsync(PracticeSession session, IQueryable<PracticeSession> replaced, CancellationToken cancellationToken)
    {
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await replaced.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, SessionStatus.Abandoned), cancellationToken);
            db.PracticeSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await reader.ResponseOfAsync(session, cancellationToken);
    }
}
