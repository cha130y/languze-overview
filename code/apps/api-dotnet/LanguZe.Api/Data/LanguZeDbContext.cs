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
            word.Property(w => w.ThaiMeaningKey).HasMaxLength(100);
            word.Property(w => w.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            word.HasIndex(w => new { w.LearnerId, w.English, w.ThaiMeaningKey }).IsUnique();
            word.HasOne<User>().WithMany().HasForeignKey(w => w.LearnerId).OnDelete(DeleteBehavior.Cascade);
            word.HasOne(w => w.Mastery).WithOne().HasForeignKey<WordMastery>(m => m.VocabularyWordId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WordOccurrence>(occurrence =>
        {
            occurrence.ToTable("word_occurrences", table => table.HasCheckConstraint(
                "word_occurrences_box_inside_photo",
                "\"box_x\" >= 0 AND \"box_y\" >= 0 AND \"box_width\" > 0 AND \"box_height\" > 0 AND \"box_x\" + \"box_width\" <= 1 AND \"box_y\" + \"box_height\" <= 1"));
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

        SchemaNames.Apply(builder);
    }
}
