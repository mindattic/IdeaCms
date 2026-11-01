using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdeaCms.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginComponentKindSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add ActivePluginsJson: stores the JSON array of Plugin ref strings selected for a page.
            migrationBuilder.AddColumn<string>(
                name: "ActivePluginsJson",
                table: "Pages",
                type: "nvarchar(max)",
                nullable: true);

            // Data rewrite: Widget is no longer a valid ContentKind (ordinal 1 is now Plugin).
            // Databases that went through the Widget era (RenamePluginKindToWidget migration) must
            // have their rows updated so DiscoveryService and PackageInstallService can parse them.
            // Plugin-classified keys (library/docs/data/components.json); all remaining Widget rows → Component.
            migrationBuilder.Sql(@"
UPDATE [InstalledPackages]
SET [Category] = 'Plugin'
WHERE [Category] = 'Widget'
  AND [Key] IN ('tooltip','outfitfont','atticfont','sacredgeometry','cyberspace',
                'navmenu','breadcrumbs','footer','pinfooter','backtotop','backhomem','sociallinks');

UPDATE [InstalledPackages]
SET [Category] = 'Component'
WHERE [Category] = 'Widget';

UPDATE [ContentDefinitions]
SET [Kind] = 'Plugin', [Category] = 'Plugin'
WHERE [Origin] = 'Package' AND [Category] = 'Widget'
  AND [Key] IN ('tooltip','outfitfont','atticfont','sacredgeometry','cyberspace',
                'navmenu','breadcrumbs','footer','pinfooter','backtotop','backhomem','sociallinks');

UPDATE [ContentDefinitions]
SET [Kind] = 'Component', [Category] = 'Component'
WHERE [Origin] = 'Package' AND [Category] = 'Widget';

-- Rewrite Widget.*→Plugin.* include tokens in Pages.BodyHtml (plugin-classified keys)
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.Tooltip','Plugin.Tooltip')   WHERE [BodyHtml] LIKE '%.Widget.Tooltip%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.OutfitFont','Plugin.OutfitFont') WHERE [BodyHtml] LIKE '%.Widget.OutfitFont%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.AtticFont','Plugin.AtticFont')  WHERE [BodyHtml] LIKE '%.Widget.AtticFont%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.SacredGeometry','Plugin.SacredGeometry') WHERE [BodyHtml] LIKE '%.Widget.SacredGeometry%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.Cyberspace','Plugin.Cyberspace') WHERE [BodyHtml] LIKE '%.Widget.Cyberspace%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.NavMenu','Plugin.NavMenu')    WHERE [BodyHtml] LIKE '%.Widget.NavMenu%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.Breadcrumbs','Plugin.Breadcrumbs') WHERE [BodyHtml] LIKE '%.Widget.Breadcrumbs%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.Footer','Plugin.Footer')      WHERE [BodyHtml] LIKE '%.Widget.Footer%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.PinFooter','Plugin.PinFooter') WHERE [BodyHtml] LIKE '%.Widget.PinFooter%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.BackToTop','Plugin.BackToTop') WHERE [BodyHtml] LIKE '%.Widget.BackToTop%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.BackHomeM','Plugin.BackHomeM') WHERE [BodyHtml] LIKE '%.Widget.BackHomeM%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.SocialLinks','Plugin.SocialLinks') WHERE [BodyHtml] LIKE '%.Widget.SocialLinks%';
-- Rewrite remaining Widget.*→Component.* (inline-placed)
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Widget.','Component.') WHERE [BodyHtml] LIKE '%.Widget.%';

-- Rewrite Widget.*→Plugin.* in WidgetPlacementSettings.WidgetRef (plugin-classified keys)
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.tooltip','Plugin.tooltip')        WHERE [WidgetRef] LIKE 'Widget.tooltip%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.outfitfont','Plugin.outfitfont')  WHERE [WidgetRef] LIKE 'Widget.outfitfont%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.atticfont','Plugin.atticfont')    WHERE [WidgetRef] LIKE 'Widget.atticfont%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.sacredgeometry','Plugin.sacredgeometry') WHERE [WidgetRef] LIKE 'Widget.sacredgeometry%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.cyberspace','Plugin.cyberspace')  WHERE [WidgetRef] LIKE 'Widget.cyberspace%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.navmenu','Plugin.navmenu')        WHERE [WidgetRef] LIKE 'Widget.navmenu%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.breadcrumbs','Plugin.breadcrumbs') WHERE [WidgetRef] LIKE 'Widget.breadcrumbs%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.footer','Plugin.footer')          WHERE [WidgetRef] LIKE 'Widget.footer%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.pinfooter','Plugin.pinfooter')    WHERE [WidgetRef] LIKE 'Widget.pinfooter%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.backtotop','Plugin.backtotop')    WHERE [WidgetRef] LIKE 'Widget.backtotop%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.backhomem','Plugin.backhomem')    WHERE [WidgetRef] LIKE 'Widget.backhomem%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]=REPLACE([WidgetRef],'Widget.sociallinks','Plugin.sociallinks') WHERE [WidgetRef] LIKE 'Widget.sociallinks%';
-- Rewrite remaining Widget.*→Component.*
UPDATE [WidgetPlacementSettings] SET [WidgetRef]='Component.'+SUBSTRING([WidgetRef],8,LEN([WidgetRef])) WHERE [WidgetRef] LIKE 'Widget.%';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the Plugin/Component→Widget data rewrites before dropping the column.
            migrationBuilder.Sql(@"
-- Restore InstalledPackages: Plugin/Component rows back to Widget
UPDATE [InstalledPackages] SET [Category] = 'Widget' WHERE [Category] IN ('Plugin', 'Component');

-- Restore ContentDefinitions: Plugin/Component Package-origin rows back to Widget
UPDATE [ContentDefinitions] SET [Kind] = 'Widget', [Category] = 'Widget'
WHERE [Origin] = 'Package' AND [Category] IN ('Plugin', 'Component');

-- Restore Pages.BodyHtml Plugin tokens (per-key — mirrors Up(); avoids corrupting prose that contains 'Plugin.')
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.Tooltip','Widget.Tooltip')               WHERE [BodyHtml] LIKE '%Plugin.Tooltip%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.OutfitFont','Widget.OutfitFont')         WHERE [BodyHtml] LIKE '%Plugin.OutfitFont%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.AtticFont','Widget.AtticFont')           WHERE [BodyHtml] LIKE '%Plugin.AtticFont%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.SacredGeometry','Widget.SacredGeometry') WHERE [BodyHtml] LIKE '%Plugin.SacredGeometry%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.Cyberspace','Widget.Cyberspace')         WHERE [BodyHtml] LIKE '%Plugin.Cyberspace%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.NavMenu','Widget.NavMenu')               WHERE [BodyHtml] LIKE '%Plugin.NavMenu%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.Breadcrumbs','Widget.Breadcrumbs')       WHERE [BodyHtml] LIKE '%Plugin.Breadcrumbs%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.Footer','Widget.Footer')                 WHERE [BodyHtml] LIKE '%Plugin.Footer%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.PinFooter','Widget.PinFooter')           WHERE [BodyHtml] LIKE '%Plugin.PinFooter%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.BackToTop','Widget.BackToTop')           WHERE [BodyHtml] LIKE '%Plugin.BackToTop%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.BackHomeM','Widget.BackHomeM')           WHERE [BodyHtml] LIKE '%Plugin.BackHomeM%';
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Plugin.SocialLinks','Widget.SocialLinks')       WHERE [BodyHtml] LIKE '%Plugin.SocialLinks%';
-- Restore Component tokens (LIKE anchors to {{ delimiter so prose like 'Component.X' is not touched)
UPDATE [Pages] SET [BodyHtml]=REPLACE([BodyHtml],'Component.','Widget.')
  WHERE [BodyHtml] LIKE '%{{ Component.%' OR [BodyHtml] LIKE '%{{Component.%';

-- Restore WidgetPlacementSettings.WidgetRef: Plugin.*/Component.* → Widget.*
UPDATE [WidgetPlacementSettings] SET [WidgetRef]='Widget.'+SUBSTRING([WidgetRef],8,LEN([WidgetRef])) WHERE [WidgetRef] LIKE 'Plugin.%';
UPDATE [WidgetPlacementSettings] SET [WidgetRef]='Widget.'+SUBSTRING([WidgetRef],11,LEN([WidgetRef])) WHERE [WidgetRef] LIKE 'Component.%';
");

            migrationBuilder.DropColumn(
                name: "ActivePluginsJson",
                table: "Pages");
        }
    }
}
