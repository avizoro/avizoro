using Microsoft.EntityFrameworkCore;

namespace IntelligenceKit.Server.Data;

public class IntelligenceDbContext : DbContext
{
    public IntelligenceDbContext(DbContextOptions<IntelligenceDbContext> options)
        : base(options)
    {
    }

    public DbSet<StoredEvent> Events => Set<StoredEvent>();

    public DbSet<StoredScreenshot> Screenshots => Set<StoredScreenshot>();

    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<AppSession> Sessions => Set<AppSession>();

    public DbSet<TimelineEntry> TimelineEntries => Set<TimelineEntry>();

    public DbSet<SymbolFile> Symbols => Set<SymbolFile>();

    public DbSet<StoredSpan> Spans => Set<StoredSpan>();

    public DbSet<Feedback> Feedback => Set<Feedback>();

    public DbSet<AlertRule> AlertRules => Set<AlertRule>();

    public DbSet<AlertNotification> AlertNotifications => Set<AlertNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var stored = modelBuilder.Entity<StoredEvent>();
        stored.HasKey(e => e.Id);
        // Common query paths: newest-first within a project, and by event type.
        stored.HasIndex(e => new { e.ProjectId, e.ReceivedAt });
        stored.HasIndex(e => e.EventType);
        // Listing the events that belong to an issue.
        stored.HasIndex(e => new { e.ProjectId, e.Fingerprint });
        // Merging a session's errors into its timeline.
        stored.HasIndex(e => e.SessionId);

        var screenshot = modelBuilder.Entity<StoredScreenshot>();
        screenshot.HasKey(s => s.EventId);

        var issue = modelBuilder.Entity<Issue>();
        issue.HasKey(i => i.Id);
        // One issue per (project, fingerprint); also the upsert lookup path.
        issue.HasIndex(i => new { i.ProjectId, i.Fingerprint }).IsUnique();
        issue.HasIndex(i => new { i.ProjectId, i.LastSeen });
        issue.Property(i => i.Status).HasMaxLength(16);
        issue.HasIndex(i => new { i.ProjectId, i.Status });
        issue.HasIndex(i => new { i.ProjectId, i.FirstRelease });

        var project = modelBuilder.Entity<Project>();
        project.HasKey(p => p.Id);
        // ProjectId is the public routing id — one project per id.
        project.HasIndex(p => p.ProjectId).IsUnique();
        // Read-side auth resolves a presented key to a project by its hash.
        project.HasIndex(p => p.ReadKeyHash);
        // Ingest validates the (ProjectId, ProjectKey) pair.
        project.HasIndex(p => new { p.ProjectId, p.ProjectKey });

        var session = modelBuilder.Entity<AppSession>();
        session.HasKey(s => s.Id);
        session.Property(s => s.Status).HasMaxLength(16);
        // Release-health reads: a project's sessions in a time window, per release.
        session.HasIndex(s => new { s.ProjectId, s.Started });
        session.HasIndex(s => new { s.ProjectId, s.Release });
        session.Property(s => s.DeviceModel).HasMaxLength(200);
        session.Property(s => s.Manufacturer).HasMaxLength(200);
        session.Property(s => s.OperatingSystem).HasMaxLength(200);

        var timeline = modelBuilder.Entity<TimelineEntry>();
        timeline.HasKey(t => t.Id);
        timeline.Property(t => t.Category).HasMaxLength(64);
        timeline.Property(t => t.Level).HasMaxLength(16);
        timeline.Property(t => t.Message).HasMaxLength(TimelineLimits.MessageLength);
        // Reading one session in order; idempotent ingest / screenshot scope by batch; retention.
        timeline.HasIndex(t => new { t.SessionId, t.Sequence });
        timeline.HasIndex(t => t.BatchId);
        timeline.HasIndex(t => new { t.ProjectId, t.ReceivedAt });

        var symbol = modelBuilder.Entity<SymbolFile>();
        symbol.HasKey(s => s.Id);
        symbol.Property(s => s.Kind).HasMaxLength(32);
        symbol.Property(s => s.Key).HasMaxLength(300);
        symbol.HasIndex(s => new { s.Kind, s.Key });

        var span = modelBuilder.Entity<StoredSpan>();
        span.HasKey(s => s.Id);
        span.Property(s => s.Operation).HasMaxLength(64);
        span.Property(s => s.Name).HasMaxLength(300);
        span.HasIndex(s => new { s.ProjectId, s.Start });

        var feedback = modelBuilder.Entity<Feedback>();
        feedback.HasKey(f => f.Id);
        feedback.HasIndex(f => f.EventId);
        feedback.HasIndex(f => new { f.IssueId, f.CreatedAt });
        feedback.HasIndex(f => new { f.ProjectId, f.CreatedAt });

        var rule = modelBuilder.Entity<AlertRule>();
        rule.HasKey(r => r.Id);
        rule.HasIndex(r => r.ProjectId);

        var notification = modelBuilder.Entity<AlertNotification>();
        notification.HasKey(n => n.Id);
        // Cooldown lookup (rule, issue, newest) and the history feed (newest first).
        notification.HasIndex(n => new { n.RuleId, n.IssueId, n.CreatedAt });
        notification.HasIndex(n => new { n.ProjectId, n.CreatedAt });
    }
}
