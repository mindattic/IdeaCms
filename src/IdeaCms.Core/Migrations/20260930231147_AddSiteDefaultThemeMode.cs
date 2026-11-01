using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteDefaultThemeMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultThemeMode",
                table: "Sites",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "light");

            // The standalone "Light" and "Dark" themes were merged into one theme, "ideas", with light
            // and dark modes. Any site/page still pointing at the old keys would otherwise silently
            // fall back to the unrelated built-in "bootstrap" theme once those keys stop resolving.
            // Wrapped in dynamic SQL (EXEC) because DefaultThemeMode is added by the AddColumn call
            // just above: SQL Server compiles a whole batch upfront, so a plain (non-dynamic) UPDATE
            // referencing a column added earlier in the SAME batch still fails to bind at compile time.
            migrationBuilder.Sql("EXEC(N'UPDATE Sites SET DefaultThemeKey = ''ideas'', DefaultThemeMode = ''light'' WHERE DefaultThemeKey = ''light''');");
            migrationBuilder.Sql("EXEC(N'UPDATE Sites SET DefaultThemeKey = ''ideas'', DefaultThemeMode = ''dark'' WHERE DefaultThemeKey = ''dark''');");
            migrationBuilder.Sql("UPDATE Pages SET ThemeKey = 'ideas' WHERE ThemeKey IN ('light', 'dark');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultThemeMode",
                table: "Sites");
        }
    }
}
