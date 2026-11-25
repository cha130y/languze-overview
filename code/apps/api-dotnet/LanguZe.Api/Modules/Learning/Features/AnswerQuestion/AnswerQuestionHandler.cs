using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Learning.Domain;
using LanguZe.Api.Modules.Learning.Shared;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LanguZe.Api.Modules.Learning.Features.AnswerQuestion;

/// <summary>
/// Answering a question (attempts.service.ts; FR-032 to FR-034). The attempt, what it did to the
/// word's mastery, and the XP it earned are written together or not at all (NFR-011).
/// </summary>
internal sealed class AnswerQuestionHandler(LanguZeDbContext db, SessionReader reader, TimeProvider clock)
{
    public async Task<AnswerResultResponse> HandleAsync(Guid learnerId, Guid sessionId, Guid questionId, AnswerRequest request, CancellationToken cancellationToken)
    {
        var question = await db.SessionQuestions
            .Include(row => row.Session)
            .Include(row => row.Attempt)
            .FirstOrDefaultAsync(row => row.Id == questionId && row.SessionId == sessionId && row.Session.LearnerId == learnerId, cancellationToken)
            // A question in somebody else's session is missing, not forbidden (FR-008).
            ?? throw new AppException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "This question does not exist.");

        var occurrence = question.OccurrenceId is { } occurrenceId
            ? await db.WordOccurrences.AsNoTracking().FirstOrDefaultAsync(row => row.Id == occurrenceId, cancellationToken)
            : null;
        var word = question.VocabularyWordId is { } wordId
            ? await db.VocabularyWords.Include(row => row.Mastery).FirstOrDefaultAsync(row => row.Id == wordId, cancellationToken)
            : null;

        // The word was removed, or its world deleted, since the session started (UC-031 2b).
        if (occurrence is null || word is null)
        {
            throw new AppException(ErrorCodes.QuestionUnavailable, StatusCodes.Status409Conflict, "This word is no longer in the world.");
        }

        var feedback = new WordFeedbackResponse(word.English, occurrence.ThaiMeaning, occurrence.ExampleSentence);

        // Already answered: the recorded result again, with nothing written (FR-034, S5). This
        // comes before the session's own state, so pressing again after the last question shows
        // the result rather than an error.
        if (question.Attempt is { } recorded)
        {
            return await RecordedAsync(recorded, feedback, sessionId, cancellationToken);
        }

        // A session left for a day is over, whatever its row still says (V17).
        if (question.Session.Status != SessionStatus.InProgress || reader.IsStale(question.Session))
        {
            await reader.AbandonAsync(sessionId, cancellationToken);
            throw new AppException(ErrorCodes.SessionClosed, StatusCodes.Status409Conflict, "This session is no longer open.");
        }

        var dontKnow = request.DontKnow == true;

        // Giving up keeps no answer text, which the database also insists on.
        var answerText = dontKnow ? null : request.Answer ?? string.Empty;
        var correct = !dontKnow && AnswerCheck.IsCorrectAnswer(answerText!, word.English, occurrence.AcceptedVariants ?? []);
        var before = word.Mastery is { } mastery
            ? new Mastery(Levels.StageOf(mastery.Level)!.Value, mastery.Streak, mastery.FamiliarOn)
            : null;
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
        var after = MasteryRules.Next(before, correct, DateOnly.FromDateTime(BangkokDay.StartOf(now).ToOffset(BangkokDay.Offset).DateTime));
        var xpAwarded = AnswerCheck.XpFor(correct);

        bool completed;
        try
        {
            completed = await RecordAsync(learnerId, sessionId, questionId, word.Id, occurrence.Id, answerText, dontKnow, correct, xpAwarded, before, after, now, cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two answers to one question arrived together and the database refused the second.
            // The first one counts; this shows what it recorded (FR-034).
            db.ChangeTracker.Clear();
            var first = await db.Attempts.AsNoTracking().SingleAsync(attempt => attempt.QuestionId == questionId, cancellationToken);
            return await RecordedAsync(first, feedback, sessionId, cancellationToken);
        }

        return new AnswerResultResponse(
            correct,
            dontKnow,
            AlreadyAnswered: false,
            answerText,
            MatchedVariant: correct && !AnswerCheck.IsTheWord(answerText ?? string.Empty, word.English),
            feedback,
            xpAwarded,
            new MasteryChangeResponse(before is null ? null : Levels.NameOf(before.Level), Levels.NameOf(after.Level)),
            completed,
            completed ? await reader.SummaryOfAsync(sessionId, cancellationToken) : null);
    }

    /// <summary>Writes the attempt, the word's mastery and the XP; answers whether the session is over.</summary>
    private async Task<bool> RecordAsync(
        Guid learnerId,
        Guid sessionId,
        Guid questionId,
        Guid wordId,
        Guid occurrenceId,
        string? answerText,
        bool dontKnow,
        bool correct,
        int xpAwarded,
        Mastery? before,
        Mastery after,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Attempts.Add(new Attempt
        {
            LearnerId = learnerId,
            VocabularyWordId = wordId,
            QuestionId = questionId,
            SessionId = sessionId,
            OccurrenceId = occurrenceId,
            AnswerText = answerText,
            IsDontKnow = dontKnow,
            IsCorrect = correct,
            XpAwarded = (short)xpAwarded,
            LevelBefore = before is null ? null : Levels.LevelOf(before.Level),
            LevelAfter = Levels.LevelOf(after.Level),
            AnsweredAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);

        // One mastery per word, shared by every world that contains it (FR-040), written as
        // Prisma's upsert writes it: one statement, so two answers to the word cannot collide.
        var level = Levels.LevelOf(after.Level);
        var streak = (short)after.Streak;
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO word_mastery (vocabulary_word_id, learner_id, level, streak, familiar_on, last_practised_at, updated_at)
            VALUES ({wordId}, {learnerId}, {level}, {streak}, {after.FamiliarOn}, {now}, {now})
            ON CONFLICT (vocabulary_word_id) DO UPDATE SET level = EXCLUDED.level, streak = EXCLUDED.streak,
                familiar_on = EXCLUDED.familiar_on, last_practised_at = EXCLUDED.last_practised_at, updated_at = EXCLUDED.updated_at
            """,
            cancellationToken);

        if (xpAwarded > 0)
        {
            // Incremented rather than recounted, so XP never falls (FR-060, V4).
            await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO learner_xp (learner_id, total_xp, updated_at) VALUES ({learnerId}, {xpAwarded}, {now})
                ON CONFLICT (learner_id) DO UPDATE SET total_xp = learner_xp.total_xp + EXCLUDED.total_xp, updated_at = EXCLUDED.updated_at
                """,
                cancellationToken);
        }

        // A session ends when nothing is left to ask. Questions whose word was removed do not
        // count: they can never be answered.
        var left = await db.SessionQuestions.CountAsync(
            row => row.SessionId == sessionId && row.Attempt == null && row.OccurrenceId != null,
            cancellationToken);
        if (left == 0)
        {
            await db.PracticeSessions.Where(row => row.Id == sessionId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Status, SessionStatus.Completed).SetProperty(row => row.CompletedAt, now),
                    cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return left == 0;
    }

    /// <summary>The result of an answer already recorded, with the answer that counted and no XP a second time (FR-034).</summary>
    private async Task<AnswerResultResponse> RecordedAsync(Attempt attempt, WordFeedbackResponse word, Guid sessionId, CancellationToken cancellationToken)
    {
        var status = await db.PracticeSessions.Where(session => session.Id == sessionId).Select(session => session.Status).SingleAsync(cancellationToken);
        var completed = status == SessionStatus.Completed;
        var before = Levels.StageOf(attempt.LevelBefore);
        return new AnswerResultResponse(
            attempt.IsCorrect,
            attempt.IsDontKnow,
            AlreadyAnswered: true,
            attempt.AnswerText,
            MatchedVariant: attempt.IsCorrect && !AnswerCheck.IsTheWord(attempt.AnswerText ?? string.Empty, word.English),
            word,
            XpAwarded: 0,
            new MasteryChangeResponse(before is null ? null : Levels.NameOf(before.Value), Levels.NameOf(Levels.StageOf(attempt.LevelAfter)!.Value)),
            completed,
            completed ? await reader.SummaryOfAsync(sessionId, cancellationToken) : null);
    }
}
