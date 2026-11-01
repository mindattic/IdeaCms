namespace IdeaCms.Core.Entities;

/// <summary>
/// Registry of installed .idea packages. RESERVED for Phase 5 (runtime package loading); created now
/// so the schema never needs to change to light it up. (Category, Key, Version) is unique. Azure Blob
/// is the source of truth for the bytes; the verbatim manifest round-trips unknown fields losslessly.
/// </summary>
public sealed class InstalledPackage
{
    public int Id { get; set; }
    public Guid Uid { get; set; } = Guid.NewGuid();
    public string Category { get; set; } = "";       // Page | Theme | Component
    public string Kind { get; set; } = "";           // data | code
    public string Key { get; set; } = "";
    public int Version { get; set; } = 1;            // whole-number content version
    public string DisplayName { get; set; } = "";
    public string ManifestJson { get; set; } = "{}"; // verbatim idea.json (preserves Extra fields)
    public string BlobPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public int ManifestVersion { get; set; } = 1;    // schema version of idea.json
    public bool Enabled { get; set; } = true;
    public bool IsActiveVersion { get; set; }
    public DateTime InstalledUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Override-chain setting. Scope = Host | Site | Page. Global CSS lives at (Host, null, "css.global") —
/// editable without a deploy. (Scope, ScopeId, Key) is unique.
/// </summary>
public sealed class SettingEntry
{
    public int Id { get; set; }
    public string Scope { get; set; } = "Host";
    public int? ScopeId { get; set; }
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// System-wide admin notification (e.g. a Page tried to render a Disabled Theme/Component). DB-backed,
/// deduplicated by <see cref="DedupKey"/>. Patterned on Prose's FindingRow + Upsert.
/// </summary>
public sealed class AdminInboxMessage
{
    public int Id { get; set; }
    public Guid Uid { get; set; } = Guid.NewGuid();
    public string Severity { get; set; } = "Info";   // Info | Warning | Error
    public string Category { get; set; } = "System";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string DedupKey { get; set; } = "";        // unique; collapses repeats
    public string Status { get; set; } = "New";       // New | Read | Resolved
    public DateTime CreatedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
}
