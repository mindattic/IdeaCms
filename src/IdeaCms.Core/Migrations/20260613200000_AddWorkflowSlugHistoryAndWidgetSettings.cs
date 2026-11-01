using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowSlugHistoryAndWidgetSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Feature 3: Named-state content workflow ----
            migrationBuilder.CreateTable(
                name: "WorkflowDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                    InitialState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTransitionDefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkflowDefinitionId = table.Column<int>(type: "int", nullable: false),
                    FromState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ToState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequiredRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTransitionDefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTransitionDefs_WorkflowDefinitions_WorkflowDefinitionId",
                        column: x => x.WorkflowDefinitionId,
                        principalTable: "WorkflowDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitions_Name",
                table: "WorkflowDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTransitionDefs_WorkflowDefinitionId_FromState_ToState",
                table: "WorkflowTransitionDefs",
                columns: new[] { "WorkflowDefinitionId", "FromState", "ToState" },
                unique: true);

            // Workflow columns on Pages (temporal table — EF Core handles the temporal DDL).
            migrationBuilder.AddColumn<int>(
                name: "WorkflowDefinitionId",
                table: "Pages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowState",
                table: "Pages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // ---- Feature 4: Auto-301 slug history ----
            migrationBuilder.CreateTable(
                name: "PageSlugHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    OldSlug = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsVanity = table.Column<bool>(type: "bit", nullable: false),
                    AddedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageSlugHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageSlugHistory_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageSlugHistory_PageId_OldSlug",
                table: "PageSlugHistory",
                columns: new[] { "PageId", "OldSlug" },
                unique: true);

            // ---- Feature 2: Widget placement settings + history ----
            migrationBuilder.CreateTable(
                name: "WidgetPlacementSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    SlotName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WidgetRef = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettingsVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetPlacementSettings", x => x.Id);
                    table.UniqueConstraint("AK_WidgetPlacementSettings_Uid", x => x.Uid);
                    table.ForeignKey(
                        name: "FK_WidgetPlacementSettings_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WidgetPlacementSettingsHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlacementSettingsId = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SettingsVersion = table.Column<int>(type: "int", nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SavedByUserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetPlacementSettingsHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WidgetPlacementSettingsHistory_WidgetPlacementSettings_PlacementSettingsId",
                        column: x => x.PlacementSettingsId,
                        principalTable: "WidgetPlacementSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WidgetPlacementSettings_PageId_SlotName",
                table: "WidgetPlacementSettings",
                columns: new[] { "PageId", "SlotName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WidgetPlacementSettingsHistory_PlacementSettingsId_SettingsVersion",
                table: "WidgetPlacementSettingsHistory",
                columns: new[] { "PlacementSettingsId", "SettingsVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WidgetPlacementSettingsHistory");
            migrationBuilder.DropTable(name: "WidgetPlacementSettings");
            migrationBuilder.DropTable(name: "PageSlugHistory");
            migrationBuilder.DropTable(name: "WorkflowTransitionDefs");
            migrationBuilder.DropTable(name: "WorkflowDefinitions");

            migrationBuilder.DropColumn(name: "WorkflowDefinitionId", table: "Pages");
            migrationBuilder.DropColumn(name: "WorkflowState", table: "Pages");
        }
    }
}
