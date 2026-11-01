using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenameComponentKindToPlugin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data fix for the Component -> Plugin content-kind rename. Rows written before
            // the rename hold the string 'Component' in the ContentKind-mapped column ContentDefinitions.Kind,
            // which no longer maps to any enum member, so EF throws on the very first read (DiscoveryService).
            // This migration runs in MigrateAsync BEFORE discovery, so any existing database self-heals.
            // The Category strings (ContentDefinitions + InstalledPackages) are bumped too for consistency.
            migrationBuilder.Sql("UPDATE [ContentDefinitions] SET [Kind] = 'Plugin' WHERE [Kind] = 'Component';");
            migrationBuilder.Sql("UPDATE [ContentDefinitions] SET [Category] = 'Plugin' WHERE [Category] = 'Component';");
            migrationBuilder.Sql("UPDATE [InstalledPackages] SET [Category] = 'Plugin' WHERE [Category] = 'Component';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not cleanly reversible: after the rename, original Components and genuinely-new Plugins are
            // indistinguishable. Down is intentionally a no-op (the rename is forward-only).
        }
    }
}
