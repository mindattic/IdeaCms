using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenamePluginKindToWidget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data fix for the Plugin -> Widget content-kind rename. Rows
            // written before the rename hold the string 'Plugin' in the ContentKind-mapped column
            // ContentDefinitions.Kind, which no longer maps to any enum member, so EF throws on the very
            // first read (DiscoveryService). This migration runs in MigrateAsync BEFORE discovery, so any
            // existing database self-heals. The Category strings (ContentDefinitions + InstalledPackages)
            // are bumped too for consistency, and author include-tags ({{MindAttic.Ideas.Plugin.X}}) plus
            // uses[] entries are rewritten so already-published pages keep resolving their widgets.
            migrationBuilder.Sql("UPDATE [ContentDefinitions] SET [Kind] = 'Widget' WHERE [Kind] = 'Plugin';");
            migrationBuilder.Sql("UPDATE [ContentDefinitions] SET [Category] = 'Widget' WHERE [Category] = 'Plugin';");
            migrationBuilder.Sql("UPDATE [InstalledPackages] SET [Category] = 'Widget' WHERE [Category] = 'Plugin';");
            migrationBuilder.Sql("UPDATE [Pages] SET [BodyHtml] = REPLACE([BodyHtml], 'IdeaCms.Plugin.', 'IdeaCms.Widget.') WHERE [BodyHtml] LIKE '%IdeaCms.Plugin.%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not cleanly reversible: after the rename, original Plugins and genuinely-new Widgets are
            // indistinguishable. Down is intentionally a no-op (the rename is forward-only).
        }
    }
}
