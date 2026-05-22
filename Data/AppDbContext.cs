using FitQuest.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FitQuest.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<SessionExercise> SessionExercises => Set<SessionExercise>();
    public DbSet<AiPlanRequest> AiPlanRequests => Set<AiPlanRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(x => x.Email).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(255);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.PasswordHash).HasMaxLength(255);
            entity.HasOne(x => x.Profile)
                .WithOne(x => x.User)
                .HasForeignKey<UserProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profiles");
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.Property(x => x.ExperienceLevel).HasMaxLength(40);
            entity.Property(x => x.Goal).HasMaxLength(40);
            entity.Property(x => x.Gender).HasMaxLength(30).HasDefaultValue("not_specified");
            entity.Property(x => x.Language).HasMaxLength(20).HasDefaultValue("system");
        });

        modelBuilder.Entity<TrainingSession>(entity =>
        {
            entity.ToTable("training_sessions");
            entity.HasIndex(x => new { x.UserId, x.SessionDate });
            entity.Property(x => x.MuscleGroup).HasMaxLength(40);
            entity.Property(x => x.DayType).HasMaxLength(120);
            entity.HasOne(x => x.User)
                .WithMany(x => x.TrainingSessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionExercise>(entity =>
        {
            entity.ToTable("session_exercises");
            entity.HasIndex(x => new { x.SessionId, x.SortOrder });
            entity.Property(x => x.ExerciseName).HasMaxLength(160);
            entity.Property(x => x.Category).HasMaxLength(40);
            entity.Property(x => x.Unit).HasMaxLength(10);
            entity.HasOne(x => x.Session)
                .WithMany(x => x.Exercises)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiPlanRequest>(entity =>
        {
            entity.ToTable("ai_plan_requests");
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.Property(x => x.Provider).HasMaxLength(80);
            entity.Property(x => x.Model).HasMaxLength(120);
            entity.Property(x => x.RequestType).HasMaxLength(40);
            entity.Property(x => x.Status).HasMaxLength(40);
            entity.HasOne(x => x.User)
                .WithMany(x => x.AiPlanRequests)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        UseSnakeCaseColumns(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is ICreatedAt created)
                created.CreatedAt = now;

            if ((entry.State == EntityState.Added || entry.State == EntityState.Modified)
                && entry.Entity is IUpdatedAt updated)
                updated.UpdatedAt = now;
        }
    }

    private static void UseSnakeCaseColumns(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
        }
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0) chars.Add('_');
                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(c);
            }
        }

        return new string(chars.ToArray());
    }
}
