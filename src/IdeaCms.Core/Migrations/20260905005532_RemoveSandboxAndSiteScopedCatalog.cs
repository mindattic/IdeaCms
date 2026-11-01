using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <summary>
    /// Removes the Showroom-mode schema and the per-site catalog it needed. A showroom
    /// is a DEPLOYMENT — a separate vanilla install of Ideas — not a tenancy feature, so nothing asks a
    /// citizen which site owns it any more (M.IDEA-LAW-11).
    /// <para>
    /// The two unique indexes return to (Kind, Key, Version, Origin) / (Category, Key, Version), which also retires the filtered pair
    /// SharedRowUniqueIndexes added: those existed only because SiteId had made a nullable column part
    /// of the key, and with the column gone the plain unique index covers every row again.
    /// </para>
    /// </summary>
    public partial class RemoveSandboxAndSiteScopedCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sites_IsSandbox",
                table: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_InstalledPackages_Category_Key_Version_Shared",
                table: "InstalledPackages");

            migrationBuilder.DropIndex(
                name: "IX_InstalledPackages_Category_Key_Version_SiteId",
                table: "InstalledPackages");

            migrationBuilder.DropIndex(
                name: "IX_InstalledPackages_SiteId",
                table: "InstalledPackages");

            migrationBuilder.DropIndex(
                name: "IX_ContentDefinitions_Kind_Key_Version_Origin_Shared",
                table: "ContentDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ContentDefinitions_Kind_Key_Version_Origin_SiteId",
                table: "ContentDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_ContentDefinitions_SiteId",
                table: "ContentDefinitions");

            migrationBuilder.DropColumn(
                name: "IdleGraceMinutes",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "IsSandbox",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "LastResetUtc",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "ResetPolicy",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "InstalledPackages");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ContentDefinitions");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledPackages_Category_Key_Version",
                table: "InstalledPackages",
                columns: new[] { "Category", "Key", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentDefinitions_Kind_Key_Version_Origin",
                table: "ContentDefinitions",
                columns: new[] { "Kind", "Key", "Version", "Origin" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstalledPackages_Category_Key_Version",
                table: "InstalledPackages");

            migrationBuilder.DropIndex(
                name: "IX_ContentDefinitions_Kind_Key_Version_Origin",
                table: "ContentDefinitions");

            migrationBuilder.AddColumn<int>(
                name: "IdleGraceMinutes",
                table: "Sites",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsSandbox",
                table: "Sites",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastResetUtc",
                table: "Sites",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResetPolicy",
                table: "Sites",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "InstalledPackages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "ContentDefinitions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sites_IsSandbox",
                table: "Sites",
                column: "IsSandbox");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledPackages_Category_Key_Version_Shared",
                table: "InstalledPackages",
                columns: new[] { "Category", "Key", "Version" },
                unique: true,
                filter: "[SiteId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledPackages_Category_Key_Version_SiteId",
                table: "InstalledPackages",
                columns: new[] { "Category", "Key", "Version", "SiteId" },
                unique: true,
                filter: "[SiteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InstalledPackages_SiteId",
                table: "InstalledPackages",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentDefinitions_Kind_Key_Version_Origin_Shared",
                table: "ContentDefinitions",
                columns: new[] { "Kind", "Key", "Version", "Origin" },
                unique: true,
                filter: "[SiteId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentDefinitions_Kind_Key_Version_Origin_SiteId",
                table: "ContentDefinitions",
                columns: new[] { "Kind", "Key", "Version", "Origin", "SiteId" },
                unique: true,
                filter: "[SiteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentDefinitions_SiteId",
                table: "ContentDefinitions",
                column: "SiteId");
        }
    }
}
