using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Data;
using MindAttic.Authentication.Entities;
using IdeaCms.Core.Entities;
using MindAttic.Media;

namespace IdeaCms.Core.Data;

/// <summary>
/// The CMS database. One append-only initial migration; all later migrations are additive.
/// <see cref="Page"/> is system-versioned (temporal) for wiki-like history.
/// Also the MindAttic.Authentication data seam (<see cref="IAuthDataContext"/>): the library's
/// identity tables live here in their isolated <c>auth</c> schema (M.IDEA-LAW-9).
/// </summary>
public sealed class CmsDbContext(DbContextOptions<CmsDbContext> options) : DbContext(options), IAuthDataContext
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PageMetaTag> PageMetaTags => Set<PageMetaTag>();
    public DbSet<PageSlugHistory> PageSlugHistory => Set<PageSlugHistory>();
    public DbSet<CmsRole> CmsRoles => Set<CmsRole>();
    public DbSet<CmsUserRole> CmsUserRoles => Set<CmsUserRole>();
    public DbSet<PageRoleAccess> PageRoleAccess => Set<PageRoleAccess>();
    public DbSet<PageUserAccess> PageUserAccess => Set<PageUserAccess>();
    public DbSet<CmsContentDefinition> ContentDefinitions => Set<CmsContentDefinition>();
    public DbSet<InstalledPackage> InstalledPackages => Set<InstalledPackage>();
    public DbSet<MediaItem> Media => Set<MediaItem>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();
    public DbSet<AdminInboxMessage> AdminInbox => Set<AdminInboxMessage>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowTransitionDef> WorkflowTransitionDefs => Set<WorkflowTransitionDef>();
    public DbSet<WidgetPlacementSettings> WidgetPlacementSettings => Set<WidgetPlacementSettings>();
    public DbSet<WidgetPlacementSettingsHistory> WidgetPlacementSettingsHistory => Set<WidgetPlacementSettingsHistory>();
    public DbSet<ComponentMetadata> ComponentMetadata => Set<ComponentMetadata>();

    // MindAttic.Authentication identity tables (auth schema) — IAuthDataContext.
    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();
    public DbSet<AuthUserMfa> AuthUserMfa => Set<AuthUserMfa>();
    public DbSet<AuthRecoveryCode> AuthRecoveryCodes => Set<AuthRecoveryCode>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AuthLoginThrottle> AuthLoginThrottles => Set<AuthLoginThrottle>();
    public DbSet<AuthAuditLog> AuthAuditLog => Set<AuthAuditLog>();
    public DbSet<AuthPasswordHistory> AuthPasswordHistory => Set<AuthPasswordHistory>();
    public DbSet<AuthPasswordResetToken> AuthPasswordResetTokens => Set<AuthPasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Site>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => x.Uid);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.Key).HasMaxLength(100).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.HostBindings).HasMaxLength(1000);
            e.Property(x => x.DefaultThemeKey).HasMaxLength(120);
            e.Property(x => x.DefaultThemeMode).HasMaxLength(10).HasDefaultValue("light");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasQueryFilter(x => !x.IsDeleted);
        });

        b.Entity<Page>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => x.Uid);
            // Free-form pages are resolved by (SiteId, Slug) — never per-page routing.
            e.HasIndex(x => new { x.SiteId, x.Slug }).IsUnique();
            e.HasIndex(x => x.ThemeKey);
            e.HasIndex(x => new { x.IsPublished, x.Enabled });
            e.Property(x => x.Slug).HasMaxLength(400).IsRequired();
            e.Property(x => x.Title).HasMaxLength(400);
            e.Property(x => x.ThemeKey).HasMaxLength(120);
            e.Property(x => x.SeoTitle).HasMaxLength(400);
            e.Property(x => x.ComponentTypeName).HasMaxLength(512);
            e.Property(x => x.AssemblyName).HasMaxLength(256);
            e.Property(x => x.AuthoredByUserId).HasMaxLength(64);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.BodyTrust).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.WorkflowState).HasMaxLength(64);
            // Self-referencing tree for nav; never cascade-delete a subtree implicitly.
            e.HasOne<Page>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.NoAction);
            e.HasQueryFilter(x => !x.IsDeleted);
            // Wiki-like history: SQL Server system-versioned temporal table.
            e.ToTable("Pages", t => t.IsTemporal());
        });

        b.Entity<PageSlugHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PageId, x.OldSlug }).IsUnique();
            e.Property(x => x.OldSlug).HasMaxLength(400).IsRequired();
            e.Property(x => x.AddedByUserId).HasMaxLength(64);
            e.HasOne<Page>().WithMany(p => p.SlugHistory).HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkflowDefinition>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.InitialState).HasMaxLength(64).IsRequired();
        });

        b.Entity<WorkflowTransitionDef>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WorkflowDefinitionId, x.FromState, x.ToState }).IsUnique();
            e.Property(x => x.FromState).HasMaxLength(64).IsRequired();
            e.Property(x => x.ToState).HasMaxLength(64).IsRequired();
            e.Property(x => x.RequiredRole).HasMaxLength(100);
            e.Property(x => x.Label).HasMaxLength(200);
            e.HasOne<WorkflowDefinition>().WithMany(d => d.Transitions)
                .HasForeignKey(x => x.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WidgetPlacementSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => x.Uid);
            e.HasIndex(x => new { x.PageId, x.SlotName }).IsUnique();
            e.Property(x => x.SlotName).HasMaxLength(200).IsRequired();
            e.Property(x => x.WidgetRef).HasMaxLength(256).IsRequired();
            e.Property(x => x.ModifiedByUserId).HasMaxLength(64);
            e.HasOne<Page>().WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WidgetPlacementSettingsHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PlacementSettingsId, x.SettingsVersion }).IsUnique();
            e.Property(x => x.WidgetRef).HasMaxLength(256).IsRequired();
            e.Property(x => x.SavedByUserId).HasMaxLength(64);
            e.HasOne<WidgetPlacementSettings>().WithMany(s => s.History)
                .HasForeignKey(x => x.PlacementSettingsId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PageMetaTag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.PageId, x.Name }).IsUnique();
            e.HasOne<Page>().WithMany(p => p.MetaTags).HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ComponentMetadata>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PageUid, x.ComponentKey, x.SlotName }).IsUnique();
            e.Property(x => x.ComponentKey).HasMaxLength(120).IsRequired();
            e.Property(x => x.SlotName).HasMaxLength(200).IsRequired();
        });

        b.Entity<CmsContentDefinition>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => x.Uid);
            e.HasIndex(x => new { x.Kind, x.Key, x.Version, x.Origin }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.ClrTypeName).HasMaxLength(512);
            e.Property(x => x.AssemblyName).HasMaxLength(256);
            e.Property(x => x.AssetMount).HasMaxLength(256);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Strategy).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RenderMode).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<InstalledPackage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => x.Uid);
            e.HasIndex(x => new { x.Category, x.Key, x.Version }).IsUnique();
            e.Property(x => x.Category).HasMaxLength(16);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Key).HasMaxLength(120).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.BlobPath).HasMaxLength(1024);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.ApplyConfiguration(new MediaItemTypeConfiguration());

        b.Entity<SettingEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Scope, x.ScopeId, x.Key }).IsUnique();
            e.Property(x => x.Scope).HasMaxLength(16).IsRequired();
            e.Property(x => x.Key).HasMaxLength(200).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<AdminInboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DedupKey).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Subject).HasMaxLength(400);
            e.Property(x => x.DedupKey).HasMaxLength(450).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16);
        });

        b.Entity<CmsRole>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
        });

        b.Entity<CmsUserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.Property(x => x.UserId).HasMaxLength(64).IsRequired();
            e.HasOne<CmsRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PageRoleAccess>(e =>
        {
            e.HasKey(x => new { x.PageId, x.RoleName });
            e.Property(x => x.RoleName).HasMaxLength(100).IsRequired();
            e.HasOne<Page>().WithMany(p => p.RoleAccess).HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PageUserAccess>(e =>
        {
            e.HasKey(x => new { x.PageId, x.UserId });
            e.Property(x => x.UserId).HasMaxLength(64).IsRequired();
            e.HasOne<Page>().WithMany(p => p.UserAccess).HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
        });

        // MindAttic.Authentication identity tables — all 8 in the isolated 'auth' schema.
        b.ApplyMindAtticAuthConfiguration();
    }
}
