// ============================================================
//  ChatRelay — AppDbContext
//  Global query filters enforce tenant isolation at ORM level
// ============================================================

using Microsoft.EntityFrameworkCore;
using ChatRelay.Models;

namespace ChatRelay.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    // ── DbSets ──────────────────────────────────────────────
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WabaAccount> WabaAccounts => Set<WabaAccount>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<AutoReplyRule> AutoReplyRules => Set<AutoReplyRule>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Soft delete global filters ───────────────────────
        // These ensure deleted records are NEVER returned unless
        // explicitly bypassed with .IgnoreQueryFilters()
        modelBuilder.Entity<Tenant>()
            .HasQueryFilter(t => t.DeletedAt == null);
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => u.DeletedAt == null);
        modelBuilder.Entity<WabaAccount>()
            .HasQueryFilter(w => w.DeletedAt == null);

        // ── Auto-update UpdatedAt ────────────────────────────
        // Handled via SaveChangesAsync override below

        // ── Tenant ──────────────────────────────────────────
        modelBuilder.Entity<Tenant>(e =>
        {
            e.Property(t => t.PlanType)
                .HasConversion<string>()
                .HasMaxLength(50);
        });

        // ── User ────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Role)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.HasOne(u => u.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── WabaAccount ─────────────────────────────────────
        modelBuilder.Entity<WabaAccount>(e =>
        {
            e.Property(w => w.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.HasOne(w => w.Tenant)
                .WithMany(t => t.WabaAccounts)
                .HasForeignKey(w => w.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Contact ─────────────────────────────────────────
        modelBuilder.Entity<Contact>(e =>
        {
            e.HasOne(c => c.WabaAccount)
                .WithMany(w => w.Contacts)
                .HasForeignKey(c => c.WabaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Conversation ─────────────────────────────────────
        modelBuilder.Entity<Conversation>(e =>
        {
            e.Property(c => c.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.HasOne(c => c.WabaAccount)
                .WithMany(w => w.Conversations)
                .HasForeignKey(c => c.WabaId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Contact)
                .WithMany(ct => ct.Conversations)
                .HasForeignKey(c => c.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Message ─────────────────────────────────────────
        modelBuilder.Entity<Message>(e =>
        {
            e.Property(m => m.Status)
                .HasConversion<string>()
                .HasMaxLength(50);
            e.Property(m => m.Direction)
                .HasConversion<string>()
                .HasMaxLength(20);
            e.Property(m => m.MessageType)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.HasOne(m => m.WabaAccount)
                .WithMany(w => w.Messages)
                .HasForeignKey(m => m.WabaId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Contact)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Template ─────────────────────────────────────────
        modelBuilder.Entity<Template>(e =>
        {
            e.Property(t => t.Category)
                .HasConversion<string>()
                .HasMaxLength(50);
            e.Property(t => t.Status)
                .HasConversion<string>()
                .HasMaxLength(50);
            e.Property(t => t.HeaderType)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.HasOne(t => t.WabaAccount)
                .WithMany(w => w.Templates)
                .HasForeignKey(t => t.WabaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── MediaFile ────────────────────────────────────────
        modelBuilder.Entity<MediaFile>(e =>
        {
            e.HasOne(m => m.WabaAccount)
                .WithMany(w => w.MediaFiles)
                .HasForeignKey(m => m.WabaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AutoReplyRule ────────────────────────────────────
        modelBuilder.Entity<AutoReplyRule>(e =>
        {
            e.Property(r => r.TriggerType)
                .HasConversion<string>()
                .HasMaxLength(50);
            e.Property(r => r.MatchType)
                .HasConversion<string>()
                .HasMaxLength(50);
            e.Property(r => r.ReplyType)
                .HasConversion<string>()
                .HasMaxLength(50);

            e.HasOne(r => r.WabaAccount)
                .WithMany(w => w.AutoReplyRules)
                .HasForeignKey(r => r.WabaId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(r => r.Template)
                .WithMany()
                .HasForeignKey(r => r.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── ApiKey ───────────────────────────────────────────
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasOne(k => k.Tenant)
                .WithMany(t => t.ApiKeys)
                .HasForeignKey(k => k.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(k => k.WabaAccount)
                .WithMany(w => w.ApiKeys)
                .HasForeignKey(k => k.WabaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── WebhookEndpoint ──────────────────────────────────
        modelBuilder.Entity<WebhookEndpoint>(e =>
        {
            e.HasOne(w => w.Tenant)
                .WithMany(t => t.WebhookEndpoints)
                .HasForeignKey(w => w.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(w => w.WabaAccount)
                .WithMany(wa => wa.WebhookEndpoints)
                .HasForeignKey(w => w.WabaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── AuditLog ─────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasOne(a => a.Tenant)
                .WithMany(t => t.AuditLogs)
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // ── Auto-update UpdatedAt on every save ─────────────────
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(ct);
    }
}