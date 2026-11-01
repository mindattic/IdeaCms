---
codex: 1
project: IdeaCms
code: IDEA
layer: stories
status: living
updated: 2026-10-03
---
<!-- GENERATED from MindAttic.Tickets/data/Tickets.db by `tickets db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the tickets CLI. -->

# IdeaCms — User Stories

> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test that proves it. Test
> tokens name NUnit fixtures in `src/MindAttic.Ideas.Tests` unless noted. Build/test evidence: see
> [BIBLE §6](BIBLE.md#M.IDEA-§6).
>
> Personas: **Author** (an admin who writes pages), **Operator** (installs/manages `.idea` packages and
> sites), **Visitor** (reads a rendered page), **Citizen-Dev** (builds first-party content in `library/`),
> **Maintainer** (builds and deploys the engine).

## Epic A — Authoring & rendering a page

- **IDEA-US-A1 ✅** As an Author, I can place a `<Kind.Key />` tag (optionally `data-version="n"`) in
  free-form markup and have it resolve to the right citizen, so I compose without zones. *Given a body
  with citizen tags, When the reference parser runs, Then dotted, short-form and kind-attribute tags
  parse to the right `(Kind,Key,Version)`; an unknown kind never becomes a Page reference; any stored
  brace token is migrated to a tag.* *(verified by
  `RenderGuardTests.TryParseTag_ShortForm_WithVersionAndDottedKey`,
  `RenderGuardTests.Parse_DottedTag_CollectedWithCorrectKind`,
  `RenderGuardTests.Parse_PascalTag_WithUnrecognisedKind_StillDefaultsToComponent`,
  `LegacyTokenMigrationTests`.)* See [BIBLE §4.4](BIBLE.md#M.IDEA-§4.4).
- **IDEA-US-A2 ✅** As a Visitor, a code-page `<CmsInclude>` and a data-page tag render **identically** for
  Resolved/Missing/Disabled outcomes, so the authoring path doesn't change behavior.
  *(verified by `CmsIncludeParityTests.CmsInclude_MatchesDataPageInclude`.)*
- **IDEA-US-A3 ✅** As an Author with the `Cms.AuthorRawMarkup` claim, my page is stamped `Author` trust at
  **write time** (without it, `Untrusted`), and my deliberate JavaScript runs from the Page JS field.
  *Given a save, When the writer holds/doesn't hold the claim, Then `BodyTrust` is Author/Untrusted and
  the author Uid is captured (truncated to 64).* *(verified by
  `PageAuthoringTests.Stamp_WithClaim_IsAuthor_AndCapturesUid`, `Stamp_WithoutClaim_IsUntrusted`,
  `Stamp_TruncatesUidTo64`.)* See [M.IDEA-LAW-5](BIBLE.md#M.IDEA-LAW-5).
- **IDEA-US-A4 ✅** As a Visitor, every page body is sanitized before it renders: no script, handler,
  executable URL, frame, form or style element survives at any trust level; Author pages keep their
  citizen tags and settings, Untrusted pages keep none. *(verified by `RawContentGateTests`:
  `Author_ScriptStyleHandlersAndFrames_AreStripped`,
  `Author_KeepsCitizenTagsAndTheirSettings_ButNotHandlersOrScriptUrls`,
  `Author_KeepsSanitizedInlineStyleAndCustomProperties`, `Untrusted_DropsCitizenTags`,
  `Untrusted_StripsScriptTag`, `Untrusted_NeutralizesJavascriptUri`;
  `RawTextElementFrameTests.FreeFormPage_EmitsStyleAndScriptAsSingleMarkupFrames`.)* See
  [BIBLE §4.6](BIBLE.md#M.IDEA-§4.6).
- **IDEA-US-A5 ✅** As an Author, I can CRUD pages with soft-delete and publish/enable under the Admin
  policy. *(verified by `PageAdminServiceTests`, `AdminServiceContractTests`.)*
- **IDEA-US-A6 ✅** As a Visitor, the seeded **Frontpage** Data page renders its library components through
  its theme; the seed creates it, migrates a stock compiled copy to the Data page, and never overwrites an
  admin-authored one. *(verified by `SeededPageRenderTests`:
  `SeedBodyTags_ParseToCorrectKind_FloatingVersion`, `FrontpageBody_AllSeedTokens_ParseFromTheRealSeededPage`,
  `Seed_MigratesStockCodeFrontpage_ToDataPage_ButNeverAnAdminPage`,
  `SeedBody_InstalledTabsComponent_ExpandsToResolvedFrame`.)*
- **IDEA-US-A7 ✅** As a Visitor, navigating to the application with **no route** forwards me to the
  site's front page: the Site-scope `page.frontpage` setting, else the Host-scope one (default
  `frontpage`); the stock home page is soft-disabled by the seed. *(verified by
  `SeededPageRenderTests.Seed_SoftDisablesStockHomePage_AndNeverAnEditedOne`; the forward observed live —
  `GET /` → 302 → `/frontpage`.)*

## Epic B — Versioning, lifecycle & history

- **IDEA-US-B1 ✅** As an Author, I pin a version (`data-version="3"`) or float to the highest enabled
  version (omit it), so I juggle versions only when I care. *(verified by
  `ContentCatalogTests.FindLatest_StillPicksTheHighestVersion`,
  `PageAssetCollectorTests.PinnedVersion_ResolvesViaFind_FloatingResolvesToHighest`.)*
- **IDEA-US-B2 ✅** As an Operator, I cannot delete a version while any page pins it (body tag, `uses[]`,
  theme pin or `ActivePluginsJson`); a floating reference blocks only when deleting would orphan it.
  *(verified by `ContentLifecycleServiceTests`: `PinnedVersion_AlwaysBlocks_AndListsSlug`,
  `FloatingReference_BlocksOnlyWhenItWouldOrphan`, `DisabledOrUnpublishedPage_IsNotABlockingReference`,
  `ActivePluginsJson_VersionedPin_BlocksDeletion`, `ThemePin_Blocks_AndThemeFloatFollowsOrphanRule`; and
  `UsesDeclarationTests.DeleteGuard_BlocksDeletingAComponentACompiledPagePins`.)* See
  [HOUSE-LAW-2](../../MindAttic.HouseRules.md#HOUSE-LAW-2).
- **IDEA-US-B3 ✅** As an Operator, disabling a content version reloads the catalog so the tag then
  resolves as Disabled. *(verified by
  `ContentLifecycleServiceTests.SetEnabledFalse_ReloadsCatalog_SoResolveTagReportsDisabled`.)*
- **IDEA-US-B4 ✅** As an Operator, the EF model guards reserved columns and a delete-guard projection, so
  integrity holds at the data layer. *(verified by `CmsModelGuardTests`.)*
- **IDEA-US-B5 ✅** As an Operator, I can inspect and roll back to any prior page state via temporal
  history in the Admin Page History panel. *(verified by `PageHistoryServiceTests`:
  `RestoreAsync_CopiesSnapshotContentFields_OntoCurrentPage`, `RestoreAsync_ReStampsTrust_FromRestoringUserClaims`,
  `RestoreAsync_NonAdminUser_StampsUntrusted`, `RestoreAsync_UnknownPage_ReturnsFalse`; and the live
  `PageHistorySqlServerTests.GetHistoryAsync_OnSqlServer_ReturnsOrderedTemporalVersions` ([Explicit],
  LocalDB).)*

## Epic C — Degradation & the Admin Inbox

- **IDEA-US-C1 ✅** As an Operator, a missing/disabled dependency raises a deduped Admin Inbox message that
  collapses recurrences and reopens after resolution. *(verified by `AdminInboxServiceTests`:
  `RaiseAsync_SameDedupKey_CollapsesToOneRow`, `RaiseAsync_AfterResolve_ReopensToNew`,
  `UnreadCount_CountsOnlyNew`.)*
- **IDEA-US-C2 ✅** As a Visitor, the render thread never throws on a bad reference — it degrades to a
  placeholder that links to `/admin/upload?missing=<reference>` and fire-and-forgets the alert.
  *(verified by `RenderGuardTests`: `MissingInclude_RendersPlaceholder_AndRaisesOneMissingAlert`,
  `DisabledInclude_RendersPlaceholder_AndRaisesOneDisabledAlert`,
  `MissingPlaceholder_LinksToAdminUpload_WithTheMissingKey`.)* See [M.IDEA-LAW-7](BIBLE.md#M.IDEA-LAW-7).

## Epic D — The `.idea` package & install

- **IDEA-US-D1 ✅** As a Citizen-Dev, the `.idea` manifest kernel reads and validates with explicit errors,
  rejecting host assemblies in `bin/` and retired categories. *(verified by `ManifestReaderTests`,
  `ManifestValidatorTests`, `IdeaArchiveReaderTests`.)*
- **IDEA-US-D2 ✅** As a Citizen-Dev, packing is reflection-only and lossless/forward-compatible, with
  SHA-256 integrity and a zip-slip-guarded reader. *(verified by `PackerTests`, `ManifestAssetPackerTests`,
  `Sha256HasherTests`, `PackageExtractorTests`.)* See [HOUSE-LAW-5](../../MindAttic.HouseRules.md#HOUSE-LAW-5).
- **IDEA-US-D3 ✅** As an Operator, the whole-number version/collision resolver picks the active version,
  refuses downgrades, and refuses a compiled-citizen collision without confirmation. *(verified by
  `PackageVersionResolverTests`.)*
- **IDEA-US-D4 ✅** As an Operator, installing a `.idea` is idempotent: it registers the `InstalledPackage`
  row + a mirrored catalog row, retains prior versions on upgrade, soft-disables, and reloads the catalog.
  *(verified by `PackageInstallServiceTests`, `SeedOnInstallTests`.)*
- **IDEA-US-D5 ✅** As an Operator, a package blob is kept verbatim in a blob store for re-share/rollback.
  *(verified by `LocalFilePackageBlobStoreTests`.)*
- **IDEA-US-D6 ✅** As an Operator, a local folder source discovers packable `.idea` candidates.
  *(verified by `LocalFolderPackageSourceTests`.)*
- **IDEA-US-D7 ✅** As an Operator, only a `.idea` signed by the host-trusted certificate installs, whatever
  path its bytes took. *(verified by `PackageSignerTests`; `PackageInstallServiceTests`:
  `Install_UnsignedPackage_ThrowsPackageSignatureException_NoRowsWritten`,
  `Install_TamperedPackage_ThrowsPackageSignatureException_NoRowsWritten`,
  `Install_UntrustedSigner_ThrowsPackageSignatureException`; `VaultPackageSigningTrustTests`.)* See
  [M.IDEA-LAW-10](BIBLE.md#M.IDEA-LAW-10).
- **IDEA-US-D8 ✅** As an Operator, re-publishing the same version with different content is a conflict I
  am told about, not a silent no-op, while a re-sign of unchanged content is a no-op. *(verified by
  `PackageInstallServiceTests.Install_HashConflict_ThrowsInstallException_AndRaisesAdminInboxAlert`,
  `PackageInstallServiceTests.Install_SameVersion_SameContent_ReSignedDifferently_IsStillANoOp`,
  `PackageVersionResolverTests.SameVersion_DifferentHash_IsAConflict_NotANoOp`.)*
- **IDEA-US-D9 ✅** As an Operator, a package whose `requires[]` is unmet, or whose `minHostVersion` exceeds
  the host, is refused with nothing written. *(verified by
  `PackageInstallServiceTests.Requires_Missing_ThrowsInstallException_NoRowsWritten`,
  `PackageInstallServiceTests.Requires_AllPresent_InstallSucceeds`, `ManifestValidatorTests`.)*
- **IDEA-US-D10 ✅** As an Operator, an `.idealist` entry not present locally is fetched from a NuGet feed
  (`MindAttic.Ideas.{Category}.{Key}` at `{n}.0.0`) and cached. *(verified by
  `NuGetIdeaListPackageResolverTests` against a fake fetcher; the live GitHub Packages fetch is not
  automated.)*

## Epic E — Runtime load & asset cascade

- **IDEA-US-E1 ✅** As an Operator, a `.idea` citizen loads through a per-package collectible ALC; host
  types unify by reference identity and others delegate to the default resolver. *(verified by
  `AlcAwareTypeResolverTests`, `CmsPackageLoadContextTests`.)* See [M.IDEA-LAW-6](BIBLE.md#M.IDEA-LAW-6).
- **IDEA-US-E2 ✅** As an Author, a page's citizen css/scripts are ordered, deduped, and hoisted into
  `<head>`, fed by the manifest→`Extra` data path. *(verified by `PageAssetCollectorTests`,
  `AssetDataPathTests.Install_Then_Reload_SurfacesManifestCssScripts_OntoDescriptorExtra`,
  `UsesDeclarationTests.Collect_FromUses_HoistsReferencedCitizenAssets`.)*
- **IDEA-US-E3 ✅** As a Citizen-Dev, a `[Uses]`/`uses[]` declaration parses (bare floats, pinned,
  case-insensitive kind, rejects malformed) and drives hoisting + the delete-guard. *(verified by
  `UsesDeclarationTests.TryParseUse_BareKey_FloatsToLatest`, `UsesDeclarationTests.TryParseUse_RejectsMalformed`.)*
- **IDEA-US-E4 ✅** As an Operator, a corrupt manifest during reload doesn't abort the reload — it leaves
  that descriptor's `Extra` null. *(verified by `AssetDataPathTests.CorruptManifestJson_DoesNotAbortReload_LeavesThatExtraNull`.)*
- **IDEA-US-E5 ✅** As an Author, tiers win by cascade layer — Global → Theme → Page → Component — so I never
  need `!important` to beat a lower tier, and a Component keeps its own look on any page. *(verified by
  `CmsHeadCssLayerTests.LayerOrderStatement_IsEmittedOnceAndFirst_EvenWhenAllTiersAreEmpty`,
  `CmsHeadCssLayerTests.ComponentLayer_IsDeclaredAfterPageLayer_SoComponentWinsPrecedence`,
  `FreeFormPageCssTests`.)* See [M.IDEA-LAW-4](BIBLE.md#M.IDEA-LAW-4).
- **IDEA-US-E6 ✅** As an Author, saving Untrusted page CSS collapses same-selector duplicate blocks into one
  without touching anything else, and I am warned about a shorthand that overrides some of its longhands.
  *(verified by `CssConflictMergerTests`, `CssShorthandLinterTests`.)*

## Epic F — Admin, CLI and authoring tools

- **IDEA-US-F1 ✅** As an Operator, the `ma-idea` CLI can pack / inspect / list / install / verify / sign /
  nupkg. *(CLI in `src/MindAttic.Ideas.Sdk`; pack/validate/sign paths covered by `PackerTests`,
  `ManifestValidatorTests`, `PackageSignerTests`, `ArgParserTests`.)*
- **IDEA-US-F2 ✅** As an Operator, the Admin can enable/disable/guarded-delete content definitions and
  triage the Admin Inbox under the Admin policy. *(verified by `AdminServiceContractTests`,
  `UsersAdminContractTests`, `IdeasClaimsAugmentorTests`.)*
- **IDEA-US-F3 ✅** As an Author, I have a theme picker, a component palette, an Assets panel and a
  Packages panel (installed blobs with content hash and admin-protected download); roles are managed at
  `/users`. *(verified by `AdminAssignmentTests`: `PluginToken_PinnedVersion_ParsesBack`,
  `ThemeToken_PinnedVersion_ParsesBack`, `CatalogFilter_Theme_ReturnsOnlyThemes`,
  `CatalogFilter_Plugin_ReturnsOnlyPlugins`; `PackageRegistryServiceTests`.)*
- **IDEA-US-F4 ✅** As an Operator, I sign in via **MindAttic.Authentication**; the Admin role carries the
  `Cms.AuthorRawMarkup` claim. *(verified by `IdeasClaimsAugmentorTests`, `CmsDbContextAuthModelTests`;
  see [HOUSE-LAW-7](../../MindAttic.HouseRules.md#HOUSE-LAW-7).)*
- **IDEA-US-F5 ✅** As a Visitor, a real packed `.idea` renders end-to-end through the running host.
  *(verified by `RenderPipelineTests`: `Install_ThenReload_ThenExpand_ProducesResolvedFrame`,
  `Install_ThenExpand_UnknownToken_ProducesMissingFrame`; live renders serve `/_ideas/...` mounts with
  zero placeholders.)*
- **IDEA-US-F6 ✅** As a Citizen-Dev, a compiled citizen's declared `StylesheetUrls`/`ScriptUrls` are
  harvested into `<head>` through the same `PageAssetCollector` path as package citizens. *(verified by
  `PageAssetsTests`: `CompiledPlugin_AllAssetsOf_HarvestsViaActivator`,
  `CompiledPlugin_UnresolvableType_ReturnsEmpty`, `PackagePlugin_AllAssetsOf_DelegatesToMountedManifestAssets`.)*
- **IDEA-US-F7 ✅** As an Operator, official content lives in the first-party library and standalone
  frontends collapse into Pages: mindattic.com is the `frontpage` Data page and Legion.Frontend is the
  `personas` page, whose body is one `<Component.LegionPersonas />` tag. *(verified by
  `SeededPageRenderTests.Seed_CreatesPersonasPage_CollapsingLegionFrontendIntoOneToken`; `/personas` and
  `/frontpage` render live with zero placeholders.)*
- **IDEA-US-F8 ✅** As an Author, I edit pages with **Monaco** and catalog-driven completion of citizen
  tags; tag attributes bind to typed `[Parameter]`s. *(verified by `MonacoEditorTokenTests`:
  `IntelliSenseTag_ParsesBackViaIncludeReferenceParser`,
  `IntelliSenseTag_InsertedInBody_ParsedByIncludeReferenceParser`; `IncludeAttributeCoercionTests`
  (incl. `Expand_TagAttributes_BindTyped_AndLeaveUnmatchedRaw`); live Monaco interaction is
  browser-tested.)*
- **IDEA-US-F9 ✅** As an Author, saving a page tells me about unresolved tags, undeclared attributes,
  badly typed values and anything the sanitizer would strip; a library citizen with unsafe JS/CSS or
  malformed settings cannot be packed; CI checks every shipped package and seed page. *(verified by
  `PageMarkupValidatorTests`, `CitizenValidatorTests`,
  `ShippedContentValidationTests.EveryShippedPackage_PassesCitizenValidation`,
  `ShippedContentValidationTests.EverySeedPage_ValidatesAgainstTheShippedPackages`.)*
- **IDEA-US-F11 ✅** As a Citizen-Dev, a package that hard-codes its own asset URLs (Cyberspace's
  circuitboard textures) uses the mount the host serves it under, `/_ideas/{Category}/{key}/{version}`,
  and every file it names is in its assets; a stale category such as the retired `Widget` fails CI.
  *(verified by `ShippedContentValidationTests.EveryShippedPackage_SelfReferencingAssetUrls_MatchItsMountAndExist`,
  `EveryLibrarySource_SelfReferencingAssetUrls_MatchItsMountAndExist`,
  `SelfReferencingAssetUrlCheck_FlagsAWrongCategory`.)*
- **IDEA-US-F10 ✅** As a user who forgot my password, I follow "Forgot password?" on `/login`, get an
  emailed link to this site's `/account/reset` page (absolute, from `MindAttic:Auth:Reset:PublicBaseUrl`:
  `https://localhost:7207` in Development; the company site and the demo each set their own in
  `infra/main.bicep`), and setting a new password there replaces the old one. Mail is sent only when the
  Vault `Notifications` SMTP settings are configured. *(verified by
  `PasswordResetFlowTests.RequestReset_EmailsAnAbsoluteLinkToTheResetPage_WhichResetsThePassword`,
  `PasswordResetFlowTests.TheResetAndForgotPages_AreAnonymous`,
  `PasswordResetFlowTests.EachAzureSite_SetsItsOwnPublicBaseUrl`.)*

## Epic G — Page authoring enhancements

- **IDEA-US-G1 ✅** As an Author, I set a page's Theme from a dropdown in the collapsible Page Properties
  panel, so theme assignment is metadata, not markup. *(verified by
  `AdminAssignmentTests.CatalogFilter_Theme_ReturnsOnlyThemes`,
  `AdminAssignmentTests.ThemeToken_PinnedVersion_ParsesBack`; the panel UI is browser-confirmed.)*
- **IDEA-US-G2 ✅** As an Author, I can set a custom SEO Title and Description for a page.
  *(verified by `PageAdminServiceTests`: `Save_WithSeoFields_PersistsThroughGetAsync`,
  `Save_WithNullSeoFields_ReturnsNullOnLoad`.)*
- **IDEA-US-G3 ✅** As a Citizen-Dev, the first-party library lives in the same git repo as the engine
  (`library/`) without coupling the two build graphs. *(Abstractions types used by library citizens are
  exercised by `PackerTests` and `ManifestAssetPackerTests`; `ma-idea verify` is green across all 53
  `.idea`s.)*
- **IDEA-US-G4 ✅** As an Author, I configure each citizen instance on a page — a component's tag
  attributes, or the theme/plugin/code-page slots — from a generated editor, and share configuration by
  Copy/Paste between instances of the same citizen. *(verified by `InstanceSettingsTests`,
  `BodyTagIndexTests`, `InstanceClipboardTests`,
  `IdeaListTests.RoundTrip_CarriesThemeAndPluginInstanceSettings`.)* See [BIBLE §4.5](BIBLE.md#M.IDEA-§4.5).
- **IDEA-US-G5 ✅** As an Author, I can move a page through a named workflow whose transitions are
  role-gated, and only the `Published` state publishes it. *(verified by `WorkflowServiceTests`.)*
- **IDEA-US-G6 ✅** As a Visitor, an old or vanity slug answers 301 to the page's current slug.
  *(verified by `SlugRedirectServiceTests`.)*
- **IDEA-US-G7 ✅** As an Author, tagless instance settings keep a version history and roll back.
  *(verified by `WidgetInstanceSettingsServiceTests`.)*

## Epic H — Plugin/Component taxonomy

- **IDEA-US-H1 ✅** As an Operator, I pick the Plugins active on a page from a checkbox list in Page
  Properties, or let the page inherit the site's `plugins.default`; an explicitly empty selection means
  none. *(verified by `EffectivePluginsTests`: `NoSelection_InheritsTheSiteDefaults`,
  `ExplicitEmptySelection_MeansNoPlugins`, `OwnSelection_Wins`;
  `ContentLifecycleServiceTests.ActivePluginsJson_VersionedPin_BlocksDeletion`.)*
- **IDEA-US-H2 ✅** As an Author, I can place `<Plugin.Tooltip />` inline in a page body to activate a
  Plugin on that page without going through the Admin selection. *(verified by
  `RenderGuardTests.Parse_DottedTag_CollectedWithCorrectKind`,
  `PageAssetsTests.CompiledPlugin_AllAssetsOf_HarvestsViaActivator`.)*
- **IDEA-US-H3 ⬜** As an Author, I can place `<Theme.Cyberspace />` inline in a page body to override the
  page's Theme for that render without changing the Page Properties selection. *(not implemented:
  `PageHost` takes the theme from the page or site only.)*
- **IDEA-US-H4 ✅** As a Citizen-Dev, Components nest: a paired tag passes its inner tags to the outer
  component as `ChildContent`. *(verified by
  `RenderGuardTests.Expander_NestedPascalTags_OuterReceivesInnerAsChildContent`.)*
- **IDEA-US-H5 ✅** As a Citizen-Dev, the library's 53 `.idea`s are Themes (7), Plugins (15) and
  Components (31), each packing clean, and a retired category is a hard validation error. *(verified by
  `ShippedContentValidationTests.EveryShippedPackage_PassesCitizenValidation`,
  `ManifestValidatorTests.RetiredCategory_IsHardError`.)*
- **IDEA-US-H6 ✅** As a Citizen-Dev, a Plugin declares whether it renders before or after the body, and
  author order is kept within each slot. *(verified by `PluginSlotTests`.)*
- **IDEA-US-H7 ✅** As a Citizen-Dev, a component can list another page's children (scoped to the site)
  and read their metadata in one query. *(verified by `PageTreeFeatureTests`:
  `ChildrenOf_PopulatesPageId_SoMetadataCanBeJoined`, `ChildrenOfSlug_ScopedToSite_ReturnsOnlyThatSitesChildren`,
  `ChildrenOfSlug_UnknownSite_FallsBackToTheUnscopedLookup`,
  `IPageTree_DefaultOverload_DelegatesToTheSlugOnlyForm`; `ComponentMetadataServiceTests`.)*

## Epic I — Media storage

> The store/endpoint fixtures — `LocalDiskMediaStoreTests`, `MediaEndpointTests`,
> `ThresholdSpillStreamTests` and `AzureBlobMediaStoreIntegrationTests` — live in the sibling
> **MindAttic.Media** repo (`src/MindAttic.Media.Tests`), where the code under test lives; the codex
> doctor scans only this repo's test tree and reports them as warnings.

- **IDEA-US-I1 ✅** As an Operator, I can point the CMS at Azure Blob Storage by setting
  `Media:Provider=azure` (plus `Media:Azure:ConnectionString` **or** `BlobServiceUri`), and every page
  keeps working untouched, because `/_media/{uid}` is the contract. An unknown provider or Azure without
  credentials fails at startup. *(verified by `MediaProviderSetupTests.NoConfiguration_KeepsTheLocalDiskStore`,
  `ProviderAzure_ReplacesTheStoreAndRegistersASigner`,
  `ProviderAzure_CarriesSignedUrlLifetimeThroughToTheEndpointOptions`,
  `ProviderAzure_WithoutCredentials_FailsClosed`, `UnknownProvider_FailsClosed`.)*
- **IDEA-US-I2 ✅** As a Visitor, I can scrub through a video on a page, because `/_media/{uid}` 302s to a
  short-lived SAS URL and Azure serves the Range requests directly. *(verified by
  `MediaEndpointTests.RedirectsToASignedUrlWhenASignerIsRegistered`,
  `FallsBackToStreamingWhenTheSignerDeclines`;
  `AzureBlobMediaStoreIntegrationTests.SignedUrlServesTheBytesAndHonoursRangeRequests`, `SignedUrlExpires`,
  `PublicReadModeHandsOutThePlainUrlRebasedOnTheCdnOrigin`.)*
- **IDEA-US-I3 ✅** As an Operator, I can upload a file far larger than memory without the app buffering
  it. *(verified by `LocalDiskMediaStoreTests.Upload_OverThreshold_SpillsToDiskWithIntactBytesAndHash`,
  `Upload_AtExactlyThreshold_StaysInline`, `ThresholdSpillStreamTests.SpillsOnceAndPreservesEveryByteInOrder`,
  `CopyAndHashMatchesAOneShotHashOverTheSameBytes`,
  `AzureBlobMediaStoreIntegrationTests.LargePayloadStreamsUpAndBackWithItsHashIntact`.)*
- **IDEA-US-I4 ✅** As a Visitor, a repeat request for an unchanged asset costs no bytes, and a non-inline
  asset downloads under its real filename. *(verified by
  `MediaEndpointTests.ServesInlinePayloadWithEtagAndRangeSupport`,
  `RepeatRequestWithMatchingEtagIsNotModified`, `ServesAByteRangeOutOfALargeSpilledPayload`,
  `NonInlineTypeIsServedAsAnAttachment`, `UnknownUidIs404`, `DeletedItemIs404`.)*
- **IDEA-US-I5 ✅** As an Operator, I can get a video into the CMS from the command line with
  `--upload-media <file…> [--folder site] [--media-type video] [--dry-run]`. *(verified by
  `UploadMediaCliTests.UploadsAVideoWithTheRightContentTypeAndMediaType`, `UploadsEveryFileUpToTheNextFlag`,
  `DryRunUploadsNothing`, `MissingFileFailsBeforeUploadingAnything`, `NoFilesIsAnError`,
  `UnknownExtensionFallsBackToOctetStream`.)*
- **IDEA-US-I6 ⬜** As an Author, I can hand the CMS **pixels instead of a file path** — paste a base64
  image (or a clipboard capture) and have it become a stored asset with a `/_media/{uid}` URL, so base64
  never reaches a page. Lands in MindAttic.Media, surfaced through the host CLI and the Admin Media panel.
  Lowest priority.
- **IDEA-US-I7 ✅** As an Operator, `--extract-media` lifts inline base64 images out of page bodies and
  stylesheets into managed assets, deduplicated by content, leaving undecodable data inline and reported.
  *(verified by `ExtractMediaCliTests`.)*

## Epic J — Azure deployment

- **IDEA-US-J1 ✅** As a Maintainer, CI can restore and publish this repo without my dev box, because every
  private MindAttic package is vendored into `lib/local-packages/` and `nuget.config` lists it first.
  *(verified by `DeploymentPackagingTests.EveryReferencedMindAtticPackageIsVendoredForCi`,
  `NugetConfigListsTheVendoredFeed`, `VendoredPackagesAreTrackedRatherThanGitIgnored`.)*
- **IDEA-US-J2 ✅** As an Operator, App Service can tell whether the site is alive, because `/_health`
  answers 200 without touching the database, as soon as the server listens, and reports whether the boot
  sequence has finished and which build is running. *(verified by
  `DeploymentPackagingTests.ProductionRequiresItsDataProtectionSettingsByName`,
  `DeployWorkflowPointsAtProjectsThatExist`, `StartupGateTests.HealthReportsReadinessAndBuild`,
  `StartupGateTests.BeforeReadinessOnlyHealthIsServed`.)*
- **IDEA-US-J3 ✅** As a Maintainer, the engine ships with no known-vulnerable dependency, and the security
  floors cannot be silently reverted. *(verified by
  `DeploymentPackagingTests.SecurityPinnedPackagesAreNotDowngraded`.)*
- **IDEA-US-J4 ✅** As an Operator, I can stand the whole estate up with one command
  (`./infra/provision.ps1 -ResourceGroup rg-mindattic-ideas`), passwordless throughout. The company site
  and the demo run on it. *(verified by `DeploymentPackagingTests`, which guards the packaging and
  configuration contract the estate depends on; live at https://mindattic.azurewebsites.net.)*
- **IDEA-US-J5 ✅** As a Maintainer, a push to `master` builds, tests, migrates and deploys both sites over
  GitHub OIDC, with deploy gated on green tests and on the migration having applied; the company site is
  proven ready on the pushed commit before the demo is touched, and a transient failure during boot is
  retried instead of crashing the container. *(verified by
  `DeploymentPackagingTests.DeployWorkflowPointsAtProjectsThatExist`,
  `DeployProvesTheCompanySiteReadyOnThisCommitBeforeTouchingTheDemo`,
  `TheServerListensBeforeTheBootSequenceAndTheBootSequenceIsRetried`, `StartupGateTests`; live:
  `azure-deploy.yml` run 37156225976 on 2026-10-03, every job green, the company site ready on its commit
  four minutes after the restart.)*
- **IDEA-US-J6 ✅** As a visitor evaluating Ideas, I can sign in to a public demo whose login is revealed
  behind Turnstile and which is wiped and re-provisioned every hour. *(verified by `DemoRevealTests`,
  `AdminBootstrapTests`, `SeedServiceTests.WithAnIdealist_SeedsOnlyTheStructuralMinimum`,
  `DeploymentPackagingTests.DemoResetStopsTheDemoAndPinsTheNewPasswordBeforeReplacingItsDatabase`; the
  `demo-reset.yml` run of 2026-10-03 signed in with the new password and published it.)*

## Epic K — Content portability

- **IDEA-US-K1 ✅** As a Maintainer, I can move an authored site between environments, because
  `--export-idealist` writes pages, settings, per-component metadata and media into one `.idealist` and
  `--import-idealist` applies it. *(verified by `IdeaListTests.RoundTrip_PreservesTheAuthoredPage`,
  `SlugFilter_ExportsOnlyTheMatchingSubtree`, `PageTree_SurvivesOnParentUid`, `DryRunImport_WritesNothing`.)*
- **IDEA-US-K2 ✅** As an Operator, importing into an independently seeded database **adopts** its pages
  instead of colliding with them, and importing into another site copies rather than moves.
  *(verified by `IdeaListTests.ImportAdoptsAnIndependentlySeededPage_BySlug_RatherThanDuplicatingIt`,
  `SecondImportUploadsNothingAndCreatesNothing`,
  `IntoSite_CopiesThePagesRatherThanMovingThemOffTheSiteThatHasThem`.)*
- **IDEA-US-K3 ✅** As an Author, every media reference still resolves after the move, because import
  rewrites media uids through an old→new map. *(verified by
  `IdeaListTests.MediaUidsAreRemapped_SoEveryReferenceStillResolves`.)*
- **IDEA-US-K4 ✅** As an Operator, an idealist cannot silently grant itself raw-markup trust: the import
  states how many pages carry `Author` trust and `--untrusted` downgrades them. *(verified by
  `IdeaListTests.UntrustedFlag_DowngradesAuthorTrust`,
  `AnIdeaListFromAFutureFormat_IsRefusedRatherThanPartiallyApplied`, `NotAnIdeaList_IsReportedRatherThanThrowing`.)*
- **IDEA-US-K5 ✅** As an Operator, an idealist's Packages install in order before any content, and an
  unmet page `Uses[]` fails the whole apply with nothing written. *(verified by
  `IdeaListTests.Packages_InstallInListedOrder_LaterEntryCanRequireAnEarlierOne`,
  `PackagesFailure_AbortsBeforeAnyPageIsWritten`, `PageUsesUnmet_FailsLoudly_NoPartialApply`,
  `PackageResolver_SearchesMultipleDirectoriesInOrder_FirstMatchWins`,
  `ComposeIdealist_PackagesOnly_ProducesAnEmptySiteContentFreeArtifact`.)*
- **IDEA-US-K6 ✅** As an Operator, a deployment with no idealist configured installs the whole library,
  and one with an idealist applies it and refuses to start if it fails. *(verified by
  `BootProvisioningTests`.)*

## Epic L — Multi-domain

- **IDEA-US-L1 ✅** As an Operator, one Ideas deployment can serve several domains, because
  `ISiteResolver` matches the request host against each site's `HostBindings`. *(verified by
  `SiteResolutionTests` — hostname/port/wildcard/catch-all matching, precedence, IPv6 literals, stable
  tie-breaking.)*
- **IDEA-US-L2 ✅** As an Operator, an existing single-site deployment is unaffected, because a site with no
  bindings still answers every hostname it is the default for. *(verified by
  `SiteResolutionTests.TheExistingSingleSiteInstallIsUnaffected`, `AnUnboundHostFallsBackToTheDefaultSite`.)*
- **IDEA-US-L3 ✅** As a Visitor, the right site keeps answering **after** the page goes interactive,
  because the host is read from `NavigationManager.BaseUri`. *(verified by
  `SiteResolutionTests.PageHostReadsTheRequestHostFromNavigationManager_NotHttpContext`.)*
- **IDEA-US-L4 ✅** As an Admin, I can add and bind a domain without touching SQL in **Admin → Sites**.
  *(verified by `SiteResolutionTests.CreatingASite_NormalizesItsBindings_AndDoesNotStealDefault`,
  `TwoSitesCannotClaimTheSameHostname`, `TheDefaultSiteCannotBeDeleted_AndNeitherCanOneThatStillHasPages`,
  `MakeDefault_LeavesExactlyOneDefault`, `TheResolverAndTheAdminProbeAgree`.)*
- **IDEA-US-L5 ✅** As a Maintainer, an idealist carries exactly one site and import creates that site by
  key rather than dumping onto the default. *(verified by
  `IdeaListTests.ExportCarriesOneSite_AndImportCreatesThatSiteRatherThanDumpingOntoTheDefault`,
  `ExportTakesOnlyTheNamedSitesPages`, `ExportWithAnUnknownSiteKey_FailsRatherThanExportingTheWrongSite`.)*

## Epic M — Marketing pages

- **IDEA-US-M9 ⬜** As a reader, `/ideas` is a Data page composed of discrete components rather than one
  compiled `Component.IdeasBrochure`, so the brochure can be edited without a redeploy.

## Epic N — Themes

- **IDEA-US-N1 🟡** As a Visitor, every theme has an AA-compliant light and dark palette; the site's default
  mode applies on first visit, my toggle choice persists, and neither flashes the wrong mode. *(no
  automated test: contrast was checked by hand with a WCAG relative-luminance script against every theme ×
  mode × text/muted/link/button pair.)*

## Priority backlog

1. **IDEA-US-N1** — an automated contrast test over every theme × mode.
2. **IDEA-US-M9** — recompose `/ideas` from components.
3. **IDEA-US-H3** — inline `<Theme.X />` override.
4. **IDEA-US-I6** — paste-to-asset media input.

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| IDEA-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| IDEA-T2 | 2026-10-13 | chore | closed | Move tools\deploy.ps1 to root deploy.ps1: rebuild only on change, -Force/-Launch, generate C:\Apps\Ideas\deploy.bat (replaces launch.bat); run.bat kept | deploy.ps1 |
| IDEA-T3 | 2026-10-14 | chore | open | NUnit tests for InstanceConfigService (src/MindAttic.Ideas.Core/Services/InstanceConfigService.cs, zero coverage): GetInstancesAsync tree order page/theme/theme-Uses plugins/page plugins/body tags with correct Depth and InstanceSource, page ThemeKey overriding site DefaultThemeKey, plugin dedupe between theme [Uses] and page selection, fallback to the site plugins.default setting when ActivePluginsJson is empty (SiteDefaultPlugin), body tags only scanned for Data pages, untagged tags resolving Component then Plugin, unresolved citizens shown as (not installed) with Resolved=false; SaveSettingsAsync returning the stale-instance and not-installed errors, slot saves writing widgetRef Kind.key@version, and tag saves dropping unknown/invalid values while preserving an unchanged hand-written attribute that does not type-check - this is the admin settings editor that rewrites author BodyHtml | — |
| IDEA-T4 | 2026-10-14 | chore | open | NUnit tests for CompiledContentSource (src/MindAttic.Ideas.Core/Discovery/CompiledContentSource.cs): convention discovery from namespace MindAttic.Ideas.{Kind}.{Key} plus Vn class name (V1, V11, lowercase v), dotted key paths lowercased, Kind chosen by base class Page/Theme/Plugin/Component, [Idea] attribute overriding key/version/displayName/category/scope/renderMode, abstract and non-conforming subclasses (e.g. FreeFormPage, MissingPageHost) skipped, ReflectionTypeLoadException tolerated, AssetMount _content/{AssemblyName}, Origin Compiled and Priority 100 - this decides which compiled citizens the catalog registers | — |
| IDEA-T5 | 2026-10-14 | chore | open | NUnit tests for SeedReposCli (src/MindAttic.Ideas.Blazor/Cli/SeedReposCli.cs, 520 lines, untested): SlugFor folding dots/underscores/spaces and collapsing dashes (MindAttic.Vault to mindattic-vault), TitleFor keeping dots on lowercase domain names, FindExistingPage precedence repo-metadata identity then canonical slug then LegacySlugAliases, IsStockPluginSelection only upgrading verbatim seeder plugin sets and treating bad JSON as hand-edited, StubReadme content when README missing, rename recording PageSlugHistory and refreshing Title/SeoTitle, author theme/plugin choices never clobbered, vanished repos soft-disabled, empty repo list refusing to run; refactor HttpClient injection if needed to drive RunAsync against an in-memory DB | — |
| IDEA-T6 | 2026-10-14 | chore | open | NUnit tests for the ma-idea SDK CLI verbs (src/MindAttic.Ideas.Sdk/Program.cs, no test coverage): verify parsing uses entries Kind.key[@n], reporting MISSING/unparseable and exiting 1 vs OK 0; upgrade exit code 1 for RejectDowngrade/Blocked/HashConflict against sibling .idea files; nupkg producing MindAttic.Ideas.{Category}.{Key} at {n}.0.0 with content/{key}.idea; inspect/list output and unreadable-manifest handling; pack/sign/nupkg missing-argument and missing-file exit 2; unknown verb exit 2; disable always 2 - extract verb logic into a testable class or run the built exe from tests | — |
| IDEA-T7 | 2026-10-14 | chore | open | NUnit tests for IdeaBase URL safety and settings JSON (src/MindAttic.Ideas.Abstractions/Bases.cs): SafeUrl/IsUnsafeUrl returning # for javascript:, data:, vbscript: in any case and with leading whitespace, passing relative, http(s), mailto, tel and null/empty; document or fix obfuscated forms such as embedded tab/newline in the scheme; CurrentSettingsJson camelCasing only simple-typed [Parameter] properties, skipping Hidden settings, CaptureUnmatchedValues and complex types, enums as names - every library citizen relies on these for XSS safety | — |
| IDEA-T8 | 2026-10-14 | chore | open | NUnit tests for Demo credentials and Turnstile (src/MindAttic.Ideas.Blazor/Demo/DemoAccess.cs): KeyVaultDemoCredentialsSource 30s cache, a Key Vault failure cached as null and never thrown, OperationCanceledException propagated, single fetch under concurrent callers; CloudflareTurnstileVerifier true only for success plus matching action, remoteip sent only when present, fail-closed false on HTTP error/bad JSON; DemoOptions Enabled/RevealEnabled flags and DemoCredentials.Ready - DemoRevealTests only covers DemoReveal.HandleAsync with fakes | — |
| IDEA-T9 | 2026-10-14 | chore | open | NUnit/bUnit tests for library citizen CSS value guards (library/Components and library/Plugins V1.cs: TabBoard, Tabs, WebSnapshot Css(), Accordion/Callout/Carousel/CodeBlock Vars(), Gallery Clean(), BackHomeM/BackToTop/Footer/PinFooter/Tooltip/AtticFont/OutfitFont Css()): values containing < > { } ; are refused so settings cannot break out of the inline style block, no style/script emitted when nothing is set, TabBoard TabBoardConfig script only for non-default behaviors and alwaysShowTabPage string/bool binding, BackHomeM numbers in invariant culture - no library project has any tests today | — |
| IDEA-T10 | 2026-10-14 | chore | open | NUnit tests for LegionPersonas and Frontpage data logic (library/Components/LegionPersonas/PersonaModel.cs, library/Components/Frontpage/HubModels.cs): PersonaFiltering.Apply text search across name/archetype/worldview/background/quirk/bio case-insensitively, AND-ing facets, Enneagram filter, MinAge/MaxAge keeping null ages and ignoring defaults 0/120; PersonaData loading the embedded personas.json with distinct ordinal-sorted facets and ScoredCount; HubContent loading hub.json, attaching ecosystem.svg only to diagram sections, and throwing on a missing resource | — |
| IDEA-T11 | 2026-10-14 | chore | open | NUnit tests for IdeaListResolverFactory and CompositeIdeaListPackageResolver (src/MindAttic.Ideas.Blazor/IdeaListResolverFactory.cs, src/MindAttic.Ideas.Core/Portability/CompositeIdeaListPackageResolver.cs): factory returns the filesystem resolver alone when Ideas:NuGetFeedUrl is unset, defaults packages dir to {contentRoot}/library, composes filesystem-then-NuGet when a feed is configured; composite tries resolvers in order, first non-null stream wins, later resolvers not called, null when all miss - every .idealist entry point resolves packages through this | — |
| IDEA-T12 | 2026-10-14 | chore | open | NUnit tests for ExpandCssShorthandCli and SeedFromHtmlCli (src/MindAttic.Ideas.Blazor/Cli): expand-css-shorthand exits 1 for a missing --path, --dry-run never writes, unchanged files untouched, a file that fails CssConflictMerger.Normalize counted as failed with exit 1 and left as-is; seed from-html exits 1 with no site, dry run creates nothing, and re-runs upsert the hyperspace page and fromhtml ComponentMetadata without duplicating (make the hard-coded D: HTML path injectable to test) | — |
| IDEA-T13 | 2026-10-14 | chore | open | NUnit tests for SiteAdminService gaps (src/MindAttic.Ideas.Core/Services/SiteAdminService.cs): UpdateAsync not-found error, binding-conflict check excluding the site itself, name falling back to key, theme version <=0 becoming 1, theme mode normalized to light unless dark, bindings cleaned and de-duplicated; ListAsync ordering default first then Id with correct per-site PageCount; WhichSiteAsync null when no sites - SiteResolutionTests only covers Create/Delete/MakeDefault | — |
| IDEA-T14 | 2026-10-14 | chore | open | The SQL Server log migration in Ideas.Blazor Program.cs:131 only runs in Development; make sure production carries its logs into MindAttic.Log too | — |
| IDEA-T15 | 2026-10-15 | chore | closed | Remove duplicate command copies (/quickload, /quicksave) now centralized at the user level (~/.claude/commands) | M.FLOW-T14 |
| IDEA-T16 | 2026-10-16 | chore | closed | install.ps1 adds this repo's 8 .prose/commands stubs for the shared global commands, so any LLM client finds them | mindattic-agent-standard/install.ps1 run 2026-10-16 |
| IDEA-T17 | 2026-10-16 | chore | open | Bug hunt: run an exhaustive /bughunt over the whole repo and fix every real bug found (each fix under its own fix ticket), then hunt again; close this only when a full pass finds nothing | user request 2026-10-16: approved repo sweep |
| IDEA-T18 | 2026-10-16 | chore | open | Run the full unit test suite and fix every failure (each under its own fix ticket) until it passes; put the passing run in --ref. No test project: close this wont-do saying so | user request 2026-10-16: approved repo sweep |
| IDEA-T19 | 2026-10-16 | chore | open | Clean up code and docs: remove dead code, stale comments and outdated or duplicated docs, and simplify where behaviour doesn't change (/simplify); tests stay green | user request 2026-10-16: approved repo sweep |
| IDEA-T20 | 2026-10-16 | docs | open | Bring README.md up to date with the code as it is now (what it is, setup, usage, commands, current state), following codex-standard/README-STYLE.md; README.htm regenerated if the repo has one | user request 2026-10-16: approved repo sweep |
| IDEA-T21 | 2026-10-16 | docs | closed | Update USER_STORIES.md | — |
| IDEA-T22 | 2026-10-19 | chore | wont-do | Convert tracked source files to LF line endings via .gitattributes, per HOUSE-LAW-13 (low priority) | — |
| IDEA-T23 | 2026-10-20 | docs | open | Publish MindAttic.Ideas docs to GitHub Pages via the shared Agency DocFX pipeline: run adopt-docs-site.ps1 (README.md as home page, docs/*.md as articles, API reference only where the code builds in CI), enable Pages with build_type=workflow, set the repo homepage URL, retire tools/build-readme.ps1 and README.htm, confirm the docs Actions run is green and https://mindattic.github.io/MindAttic.Ideas/ returns 200. Depends on MAT-T8. | — |
| IDEA-T24 | 2026-10-22 | docs | closed | Update USER_STORIES.md | — |
| IDEA-T25 | 2026-10-24 | fix | open | DeploymentPackagingTests.EveryReferencedMindAtticPackageIsVendoredForCi fails on origin/main: MindAttic.Vault 6.0.0 (Blazor and Core csproj) and MindAttic.Log 3.0.0 (Core csproj) are referenced but lib/local-packages only vendors Vault 3.0.0 and Log 2.0.0, so the CI restore behind the 'ideas' MindAttic.Deploy app (azure-deploy.yml) will fail. Vendor the current .nupkgs and drop the stale ones (found by MAT-T26 sweep) | — |
| IDEA-T26 | 2026-10-24 | docs | closed | Update USER_STORIES.md | — |
| IDEA-T27 | 2026-10-27 | fix | closed | Fails on main: MindAttic.Ideas.Tests.DeploymentPackagingTests.EveryReferencedMindAtticPackageIsVendoredForCi — These packages are referenced but not vendored, so a CI restore will fail. Copy the .nupkg into lib/local-packages/: MindAttic.Vault 6.0.0 (referenced by MindAttic.Ideas.Blazor.csproj) MindAttic.Log 3.0.0 (referenced by MindAttic.Ideas.Core.csproj) MindAttic.Vault 6.0.0 (referenced by MindAttic.I... (landed on main since the last green run: tickets MAT-T68, MAT-T67 in a51e017 Rename every deploy.ps1 -> run.ps1 and deploy.bat -> run.bat (MAT-T68); 71f6e5a Every launcher opens a menu first: 1) Deploy & Run 2) Run (MAT-T67)) | passes on main@3ae015b |
| IDEA-T28 | 2026-10-27 | docs | closed | Update USER_STORIES.md | — |
| IDEA-T29 | 2026-10-31 | feature | closed | LAMP: move MindAttic.Ideas's app-level settings and keys to MindAttic.Vault.Desktop (manifest, LoadOrSeed, shared named keys, cog opens Vault) | 80fffad LampSettingsTests (suite 631/631) |
| IDEA-T30 | 2026-10-31 | fix | closed | Ideas doesn't build: MindAttic.Media 2.0.0 is compiled against EF Core 10.0.14 but Ideas pins 10.0.12 (CS1705); move Ideas' EF Core references to 10.0.14 | e285926 build |
| IDEA-T31 | 2026-10-31 | fix | closed | EveryReferencedMindAtticPackageIsVendoredForCi fails: lib/local-packages lacks MindAttic.Log 3.0.0 (Core references it), so CI restore can't find it; vendor it | 48ef31d EveryReferencedMindAtticPackageIsVendoredForCi |
| IDEA-T32 | 2026-10-31 | fix | closed | Fails on main: the build — D:\Projects\MindAttic.worktrees\MindAttic.Ideas\main-tests\src\MindAttic.Ideas.Rendering\MindAttic.Ideas.Rendering.csproj : error NU1301: The local source 'D:\Projects\MindAttic.worktrees\MindAttic.Ideas\local-feed' doesn't exist. [D:\Projects\MindAttic.worktrees\MindAttic.Ideas\main-tests\MindAt... (landed on main since the last green run: tickets M.IDEA-T29, M.IDEA-T30, M.IDEA-T31 in 4f29f37 LAMP: the admin Settings tab links to Ideas' app settings in MindAttic Vault (M.IDEA-T29); 80fffad LAMP: Ideas' app-level settings (packages, media storage, sign-in security) are edited in MindAttic Vault and projected onto the configuration keys Ideas reads, below App Service settings; today's values imported once; Vault 7 + Authentication 7 vendored (M.IDEA-T29); 07d03fb Ideas.Tests: Microsoft.Extensions.Configuration/Options to 10.0.14 to match bunit and EF (NU1605) (M.IDEA-T30); 48ef31d Vendor MindAttic.Log 3.0.0 for CI restore; Core already referenced it (M.IDEA-T31); e285926 Move Ideas' EF Core references to 10.0.14 to match MindAttic.Media 2.0.0 (CS1705 build break) (M.IDEA-T30)) | passes on main@3ae015b |
| IDEA-T33 | 2026-10-31 | fix | closed | run.ps1 never runs the published exe with --write-settings-manifest (LAMP step 7), so a redeploy leaves Vault.Desktop's Ideas manifest stale until the app is next started (found by M.VALT-T73) | 3ae015b LampSettingsTests.RunScript_WritesTheSettingsManifestFromTheDeployedExe |
| IDEA-T34 | 2026-10-31 | fix | closed | Vendored lib/local-packages MindAttic.Authentication 7.0.0 and MindAttic.Vault 7.0.0 are stale same-version builds (11:07/11:14, before M.AUTH-T25 and M.VALT-T74), so the deployed Release Ideas writes the computed auth.crypto.effectiveMaxConcurrentHashes into its Vault manifest and lacks the local-root reload fix (found by M.VALT-T73) | 5e1c0ea DeploymentPackagingTests.VendoredPackagesMatchTheLocalFeedsSameVersion |
| IDEA-T35 | 2026-10-31 | chore | closed | Commit the component .idea packages re-signed by run.ps1's deploy (library/dist and Blazor library copies) so the checkout matches what's deployed | 719cbc5 |
| IDEA-T36 | 2026-10-31 | fix | closed | Ideas' /login (MindAttic.Authentication's strict CSP) likely blocks App.razor's inline theme script and any CDN assets, as IdiotProof's did (IP-T350); serve them locally and move the script to a file | 7745c34 |
| IDEA-T37 | 2026-10-31 | chore | closed | Commit the component .idea packages run.ps1's local deploy re-signed again while redeploying for M.IDEA-T36 (library/dist and Blazor library copies), so the checkout matches what's deployed | 1ebed0b |
| IDEA-T38 | 2026-10-31 | fix | open | /login looks half-styled: its heading renders in Times New Roman and only the card is styled, with or without the auth CSP (seen during M.IDEA-T36); the auth page doesn't pick up the site's fonts/styles | M.IDEA-T36 |
| IDEA-T39 | 2026-10-31 | chore | closed | Vault tab named after the repo: 'MindAttic Ideas' -> 'MindAttic.Ideas' | M.VALT-T78 1d6046c de1ec26 |
| IDEA-T40 | 2026-10-31 | chore | closed | Commit the .idea packages run.ps1's local deploy re-signed for M.IDEA-T39 (library/dist and the Blazor library copies) | M.VALT-T78 ad9ab3d 7b09a5d |
| IDEA-T41 | 2026-10-31 | fix | closed | Fails on main: the build — D:\Projects\MindAttic.worktrees\MindAttic.Ideas\main-tests\src\MindAttic.Ideas.Rendering\MindAttic.Ideas.Rendering.csproj : error NU1301: The local source 'D:\Projects\MindAttic.worktrees\MindAttic.Ideas\local-feed' doesn't exist. [D:\Projects\MindAttic.worktrees\MindAttic.Ideas\main-tests\MindAt... (landed on main since the last green run: tickets M.IDEA-T40 in ad9ab3d Commit the component .idea packages run.ps1's local deploy re-signed (library/dist and the Blazor library copies), so the checkout matches what's deployed (M.IDEA-T40)) | passes on main@de1ec26 |
| IDEA-T42 | 2026-10-31 | fix | open | Fails on main: MindAttic.Ideas.Tests.DeploymentPackagingTests.VendoredPackagesMatchTheLocalFeedsSameVersion — Vendored packages differ from C:\LocalNuGet; copy the current .nupkg into lib/local-packages/: MindAttic.Vault.7.0.0.nupkg Assert.That(stale, Is.Empty) Expected: <empty> But was: < "MindAttic.Vault.7.0.0.nupkg" > (landed on main since the last green run: tickets M.IDEA-T39, M.IDEA-T40 in de1ec26 Admin settings panel and LAMP test comment name the Vault tab 'MindAttic.Ideas' (was 'Ideas') (M.IDEA-T39); ad9ab3d Commit the component .idea packages run.ps1's local deploy re-signed (library/dist and the Blazor library copies), so the checkout matches what's deployed (M.IDEA-T40)) | main@de1ec26 |
| IDEA-T43 | 2026-10-31 | docs | closed | Update USER_STORIES.md | — |
| IDEA-T44 | 2026-11-01 | chore | closed | Rename MindAttic.Ideas to IdeaCms repo-wide (repo, folder, namespace, files, docs) | — |
