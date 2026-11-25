using LanguZe.Api.Data.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Data;

/// <summary>
/// The one database, shared by every module the way the Prisma client is (ADR-0006,
/// section 1). Its entities arrive with the first slice that reads a table, adopted from
/// the schema Prisma created, so it holds only the tables the ported slices use. It is
/// also Identity's store and the place Data Protection keeps its keys, so cookies outlive
/// a redeploy.
/// </summary>
public sealed class LanguZeDbContext(DbContextOptions<LanguZeDbContext> options)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<AiSuspension> AiSuspensions => Set<AiSuspension>();

    public DbSet<StoredPhoto> StoredPhotos => Set<StoredPhoto>();

    public DbSet<PhotoDeletion> PhotoDeletions => Set<PhotoDeletion>();

    public DbSet<World> Worlds => Set<World>();

    public DbSet<Analysis> Analyses => Set<Analysis>();

    public DbSet<VocabularyWord> VocabularyWords => Set<VocabularyWord>();

    public DbSet<WordOccurrence> WordOccurrences => Set<WordOccurrence>();

    public DbSet<WordMastery> WordMastery => Set<WordMastery>();

    public DbSet<AiCall> AiCalls => Set<AiCall>();

    public DbSet<PhotoBlock> PhotoBlocks => Set<PhotoBlock>();

    public DbSet<PracticeSession> PracticeSessions => Set<PracticeSession>();

    public DbSet<SessionQuestion> SessionQuestions => Set<SessionQuestion>();

    public DbSet<Attempt> Attempts => Set<Attempt>();

    public DbSet<LearnerXp> LearnerXp => Set<LearnerXp>();

    public DbSet<TutorMessage> TutorMessages => Set<TutorMessage>();

    public DbSet<TutorDailyUsage> TutorDailyUsage => Set<TutorDailyUsage>();

    public DbSet<AdminAction> AdminActions => Set<AdminAction>();

    public DbSet<ClosedAccount> ClosedAccounts => Set<ClosedAccount>();

    public DbSet<TrafficEvent> TrafficEvents => Set<TrafficEvent>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampUpdatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Prisma's <c>@updatedAt</c>: <c>updated_at</c> has no default, and every write sets it.</summary>
    private void StampUpdatedAt()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified
                && entry.Metadata.FindProperty("UpdatedAt") is not null)
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.Name).HasMaxLength(50);
            user.Property(u => u.Email).HasMaxLength(254).IsRequired();
            user.Property(u => u.UserName).HasMaxLength(254);
            user.Property(u => u.NormalizedEmail).HasMaxLength(254);
            user.Property(u => u.NormalizedUserName).HasMaxLength(254);
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.EmailConfirmed).HasColumnName("email_verified").HasDefaultValue(false);
            user.Property(u => u.Role).HasDefaultValue(UserRole.Learner);
            user.Property(u => u.Status).HasDefaultValue(AccountStatus.Active);
            user.Property(u => u.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            user.Ignore(u => u.PhoneNumber);
            user.Ignore(u => u.PhoneNumberConfirmed);
            user.Ignore(u => u.TwoFactorEnabled);
            user.Ignore(u => u.LockoutEnd);
            user.Ignore(u => u.LockoutEnabled);
            user.Ignore(u => u.AccessFailedCount);
        });

        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<Session>(session =>
        {
            session.ToTable("sessions");
            session.HasIndex(s => s.UserId);
            session.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            session.Property(s => s.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        builder.Entity<AiSuspension>(suspension =>
        {
            suspension.ToTable("ai_suspensions", table =>
            {
                table.HasCheckConstraint("ai_suspensions_ends_after_it_starts", "\"ends_at\" IS NULL OR \"ends_at\" > \"starts_at\"");
                table.HasCheckConstraint("ai_suspensions_lifted_at_a_time", "\"lifted_by\" IS NULL OR \"lifted_at\" IS NOT NULL");
                table.HasCheckConstraint("ai_suspensions_lifted_after_it_starts", "\"lifted_at\" IS NULL OR \"lifted_at\" >= \"starts_at\"");
            });
            suspension.HasIndex(s => new { s.LearnerId, s.StartsAt });
            suspension.HasOne<User>().WithMany().HasForeignKey(s => s.LearnerId).OnDelete(DeleteBehavior.Cascade);
            suspension.Property(s => s.StartsAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            suspension.Property(s => s.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // lifted_by has its foreign key in the database (to users, ON DELETE SET NULL) but
            // not here: EF indexes every foreign key it knows, and would describe an index on
            // lifted_by that Prisma never made.
        });

        builder.Entity<StoredPhoto>(photo =>
        {
            photo.ToTable("stored_photos");
            photo.Property(p => p.StorageKey).HasMaxLength(200);
            photo.Property(p => p.ContentType).HasMaxLength(50);
            photo.Property(p => p.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            photo.HasIndex(p => p.StorageKey).IsUnique();
            photo.HasIndex(p => p.OwnerId);
            photo.HasOne<User>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PhotoDeletion>(deletion =>
        {
            deletion.ToTable("photo_deletions");
            deletion.Property(d => d.StorageKey).HasMaxLength(200);
            deletion.Property(d => d.LastError).HasMaxLength(500);
            deletion.Property(d => d.RequestedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            deletion.Property(d => d.Attempts).HasDefaultValue(0);
            deletion.Property(d => d.NextAttemptAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            deletion.HasIndex(d => d.NextAttemptAt);
        });

        builder.Entity<World>(world =>
        {
            world.ToTable("worlds");
            world.Property(w => w.Name).HasMaxLength(50);
            world.Property(w => w.Status).HasDefaultValue(WorldStatus.Analyzing);
            world.Property(w => w.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            world.HasIndex(w => w.PhotoId).IsUnique();
            world.HasIndex(w => w.ThumbnailId).IsUnique();
            world.HasIndex(w => new { w.LearnerId, w.CreatedAt });
            world.HasOne<User>().WithMany().HasForeignKey(w => w.LearnerId).OnDelete(DeleteBehavior.Cascade);
            world.HasOne(w => w.Photo).WithOne().HasForeignKey<World>(w => w.PhotoId).OnDelete(DeleteBehavior.SetNull);
            world.HasOne(w => w.Thumbnail).WithOne().HasForeignKey<World>(w => w.ThumbnailId).OnDelete(DeleteBehavior.SetNull);
            world.HasMany(w => w.Occurrences).WithOne().HasForeignKey(o => o.WorldId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Analysis>(analysis =>
        {
            analysis.ToTable("analyses");
            analysis.Property(a => a.Status).HasDefaultValue(AnalysisStatus.InProgress);
            analysis.Property(a => a.StartedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            analysis.HasIndex(a => new { a.LearnerId, a.StartedAt });
            analysis.HasIndex(a => new { a.Status, a.StartedAt });
            analysis.HasOne<User>().WithMany().HasForeignKey(a => a.LearnerId).OnDelete(DeleteBehavior.Cascade);

            // world_id has its foreign key in the database (to worlds, ON DELETE SET NULL) but
            // not here: EF indexes every foreign key it knows, and would describe an index on
            // world_id that Prisma never made.
        });

        builder.Entity<VocabularyWord>(word =>
        {
            word.ToTable("vocabulary_words");
            word.Property(w => w.English).HasMaxLength(40);
            word.Property(w => w.ThaiMeaning).HasMaxLength(100);
            word.Property(w => w.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            word.HasIndex(w => new { w.LearnerId, w.English }).IsUnique();
            word.HasOne<User>().WithMany().HasForeignKey(w => w.LearnerId).OnDelete(DeleteBehavior.Cascade);
            word.HasOne(w => w.Mastery).WithOne().HasForeignKey<WordMastery>(m => m.VocabularyWordId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WordOccurrence>(occurrence =>
        {
            occurrence.ToTable("word_occurrences", table =>
            {
                table.HasCheckConstraint(
                    "word_occurrences_box_inside_photo",
                    "\"box_x\" >= 0 AND \"box_y\" >= 0 AND \"box_width\" > 0 AND \"box_height\" > 0 AND \"box_x\" + \"box_width\" <= 1 AND \"box_y\" + \"box_height\" <= 1");
                // #73: an arrow is whole or absent, and inside the photo.
                table.HasCheckConstraint(
                    "word_occurrences_arrow_inside_photo",
                    "(\"point_x\" IS NULL AND \"point_y\" IS NULL AND \"tail_x\" IS NULL AND \"tail_y\" IS NULL AND \"point_source\" IS NULL)"
                    + " OR (\"point_x\" BETWEEN 0 AND 1 AND \"point_y\" BETWEEN 0 AND 1 AND \"tail_x\" BETWEEN 0 AND 1 AND \"tail_y\" BETWEEN 0 AND 1 AND \"point_source\" IS NOT NULL)");
            });
            occurrence.Property(o => o.ThaiMeaning).HasMaxLength(100);
            occurrence.Property(o => o.ExampleSentence).HasMaxLength(200);
            occurrence.Property(o => o.AcceptedVariants).HasColumnType("varchar(40)[]");
            occurrence.Property(o => o.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            occurrence.HasIndex(o => o.VocabularyWordId);
            occurrence.HasIndex(o => new { o.WorldId, o.VocabularyWordId }).IsUnique();
            occurrence.HasOne(o => o.VocabularyWord).WithMany().HasForeignKey(o => o.VocabularyWordId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WordMastery>(mastery =>
        {
            mastery.ToTable("word_mastery", table => table.HasCheckConstraint("word_mastery_streak_not_negative", "\"streak\" >= 0"));
            mastery.HasKey(m => m.VocabularyWordId);
            mastery.Property(m => m.Streak).HasDefaultValue((short)0);
            mastery.HasIndex(m => new { m.LearnerId, m.Level });
            mastery.HasOne<User>().WithMany().HasForeignKey(m => m.LearnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AiCall>(call =>
        {
            call.ToTable("ai_calls");
            call.Property(c => c.Provider).HasMaxLength(50);
            call.Property(c => c.Model).HasMaxLength(100);
            call.Property(c => c.StartedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            call.Property(c => c.ErrorCode).HasMaxLength(100);
            call.Property(c => c.RequestId).HasMaxLength(100);
            call.HasIndex(c => c.StartedAt);
        });

        builder.Entity<PhotoBlock>(block =>
        {
            block.ToTable("photo_blocks");
            block.Property(b => b.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            block.HasIndex(b => new { b.LearnerId, b.CreatedAt });
            block.HasOne<User>().WithMany().HasForeignKey(b => b.LearnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PracticeSession>(session =>
        {
            session.ToTable("practice_sessions");
            session.Property(s => s.Status).HasDefaultValue(SessionStatus.InProgress);
            session.Property(s => s.StartedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            session.HasIndex(s => new { s.LearnerId, s.StartedAt });
            session.HasOne<User>().WithMany().HasForeignKey(s => s.LearnerId).OnDelete(DeleteBehavior.Cascade);
            session.HasMany(s => s.Questions).WithOne(q => q.Session).HasForeignKey(q => q.SessionId).OnDelete(DeleteBehavior.Cascade);

            // At most one unfinished game per world and one unfinished review (FR-036): what
            // makes two sessions impossible when two starts race.
            session.HasIndex(s => new { s.LearnerId, s.WorldId }, "practice_sessions_one_open_game")
                .IsUnique()
                .HasFilter("\"status\" = 'IN_PROGRESS' AND \"kind\" = 'GAME'");
            session.HasIndex(s => s.LearnerId, "practice_sessions_one_open_review")
                .IsUnique()
                .HasFilter("\"status\" = 'IN_PROGRESS' AND \"kind\" = 'REVIEW'");

            // world_id has its foreign key in the database (to worlds, ON DELETE SET NULL) but
            // not here: EF indexes every foreign key it knows, and would describe an index on
            // world_id that Prisma never made.
        });

        builder.Entity<SessionQuestion>(question =>
        {
            question.ToTable("session_questions", table => table.HasCheckConstraint("session_questions_position_starts_at_one", "\"position\" >= 1"));
            question.HasIndex(q => new { q.SessionId, q.Position }).IsUnique();
            question.HasOne(q => q.Attempt).WithOne().HasForeignKey<Attempt>(a => a.QuestionId).OnDelete(DeleteBehavior.SetNull);

            // vocabulary_word_id and occurrence_id have their foreign keys in the database (ON
            // DELETE SET NULL) but not here, for the same reason as world_id above.
        });

        builder.Entity<Attempt>(attempt =>
        {
            attempt.ToTable("attempts", table =>
            {
                table.HasCheckConstraint("attempts_dont_know_is_blank_and_wrong", "NOT \"is_dont_know\" OR (\"answer_text\" IS NULL AND NOT \"is_correct\")");
                table.HasCheckConstraint("attempts_wrong_answers_earn_nothing", "\"xp_awarded\" >= 0 AND (\"is_correct\" OR \"xp_awarded\" = 0)");
            });
            attempt.Property(a => a.AnswerText).HasMaxLength(100);
            attempt.Property(a => a.IsDontKnow).HasDefaultValue(false);
            attempt.Property(a => a.XpAwarded).HasDefaultValue((short)0);
            attempt.Property(a => a.AnsweredAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            attempt.HasIndex(a => a.QuestionId).IsUnique();
            attempt.HasIndex(a => new { a.LearnerId, a.AnsweredAt });
            attempt.HasIndex(a => new { a.VocabularyWordId, a.AnsweredAt });
            attempt.HasOne<User>().WithMany().HasForeignKey(a => a.LearnerId).OnDelete(DeleteBehavior.Cascade);
            attempt.HasOne<VocabularyWord>().WithMany().HasForeignKey(a => a.VocabularyWordId).OnDelete(DeleteBehavior.Cascade);

            // session_id and occurrence_id have their foreign keys in the database (ON DELETE
            // SET NULL) but not here, for the same reason as world_id above.
        });

        builder.Entity<LearnerXp>(xp =>
        {
            xp.ToTable("learner_xp", table => table.HasCheckConstraint("learner_xp_not_negative", "\"total_xp\" >= 0"));
            xp.HasKey(x => x.LearnerId);
            xp.Property(x => x.TotalXp).HasDefaultValue(0);
            xp.HasOne<User>().WithOne().HasForeignKey<LearnerXp>(x => x.LearnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TutorMessage>(message =>
        {
            message.ToTable("tutor_messages", table => table.HasCheckConstraint(
                "tutor_messages_learner_message_length",
                "length(\"content\") > 0 AND (\"role\" <> 'LEARNER' OR length(\"content\") <= 1000)"));
            message.Property(m => m.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            message.HasIndex(m => new { m.LearnerId, m.CreatedAt });
            message.HasOne<User>().WithMany().HasForeignKey(m => m.LearnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TutorDailyUsage>(usage =>
        {
            usage.ToTable("tutor_daily_usage", table => table.HasCheckConstraint("tutor_daily_usage_not_negative", "\"messages_used\" >= 0"));
            usage.HasKey(u => new { u.LearnerId, u.Day });
            usage.Property(u => u.MessagesUsed).HasDefaultValue((short)0);
            usage.HasOne<User>().WithMany().HasForeignKey(u => u.LearnerId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrafficEvent>(traffic =>
        {
            traffic.ToTable("traffic_events");
            traffic.Property(t => t.Via).HasMaxLength(20);
            traffic.HasIndex(t => t.OccurredAt);
            traffic.HasIndex(t => t.AccountId);

            // No foreign key: an event outlives the account (FR-110). The triggers that keep each
            // event unchanged for 90 days are in the AddTrafficEvents migration.
        });

        builder.Entity<ClosedAccount>(closed =>
        {
            closed.ToTable("closed_accounts");
            closed.HasKey(c => c.AccountId);
            closed.Property(c => c.Email).HasMaxLength(254);
            closed.Property(c => c.ProviderAccounts).HasColumnType("varchar(300)[]");
            closed.HasIndex(c => c.ClosedAt);

            // No foreign key: the record outlives the account (FR-111). The triggers that keep
            // each record unchanged for 90 days are in the AddClosedAccounts migration.
        });

        builder.Entity<AdminAction>(action =>
        {
            action.ToTable("admin_actions", table => table.HasCheckConstraint("admin_actions_reason_length", "length(btrim(\"reason\")) > 0"));
            action.Property(a => a.Reason).HasMaxLength(500);
            action.Property(a => a.Details).HasColumnType("jsonb");
            action.Property(a => a.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            action.HasIndex(a => a.LearnerId);
            action.HasIndex(a => a.CreatedAt);

            // No foreign keys: an entry outlives the learner's account (FR-106). The triggers that
            // refuse every change to an entry are Prisma's, and EF Core has no word for them (D4).
        });

        SchemaNames.Apply(builder);
    }
}
