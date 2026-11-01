using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddComponentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComponentMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageUid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SlotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentMetadata", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComponentMetadata_PageUid_ComponentKey_SlotName",
                table: "ComponentMetadata",
                columns: new[] { "PageUid", "ComponentKey", "SlotName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComponentMetadata");
        }
    }
}
