using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPageSeoTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeoTitle",
                table: "Pages",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeoTitle",
                table: "Pages");
        }
    }
}
