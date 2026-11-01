using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Plugin.Cyberspace;

/// <summary>
/// IdeaCms.Plugin.Cyberspace.V1 — the animated console-background effects engine as a
/// self-contained Plugin. Bundles its own CSS (the effect-layer styles), the engine JS, the circuitboard
/// textures, and a bootstrap shim that points the engine at those bundled textures — all under
/// <c>/_ideas/Plugin/cyberspace/1/</c>. The Theme renders the effect-layer divs (.cyberspace-sl-fine /
/// .cyberspace-sl-coarse / .console-bg-host) and composes this Plugin; the engine paints into them.
/// circuitboard-srcs.js MUST load before console-bg.js (it reads window.__cyberspaceCircuitboardSrcs at init).
/// Every effect is a per-page instance setting (M.IDEA-§4.5), published to the engine through a
/// <c>data-ma-settings="plugin.cyberspace"</c> element the glue shim reads live.
/// </summary>
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/cyberspace/1";

    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/frontpage.css" };

    public override IReadOnlyList<string> ScriptUrls { get; } = new[]
    {
        Mount + "/circuitboard-srcs.js",
        Mount + "/loader.js",
        Mount + "/console-bg.js",
    };

    // ── Background ──────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Parallax circuitboard", Group = "Background", Order = 1, Description = "The slowly drifting circuit-board texture layers")]
    public bool Circuitboard { get; set; } = true;

    [Parameter, Setting("Circuitboard opacity ×", Group = "Background", Order = 2, Description = "Multiplier on the texture layers' opacity (1 = as designed)")]
    public double CircuitboardOpacity { get; set; } = 1;

    [Parameter, Setting("Circuitboard speed ×", Group = "Background", Order = 3, Description = "Multiplier on the parallax drift speed (0 = still)")]
    public double CircuitboardSpeed { get; set; } = 1;

    // ── Effects ─────────────────────────────────────────────────────────────────────────────────
    [Parameter, Setting("Spawn rate ×", Group = "Effects", Order = 0, Description = "Multiplier on how often effects spawn (1 = as designed)")]
    public double SpawnRate { get; set; } = 1;

    [Parameter, Setting("Crash — fatal-error popups", Group = "Effects", Order = 1)] public bool Crash { get; set; } = true;
    [Parameter, Setting("Tremor — warning popups", Group = "Effects", Order = 2)] public bool Tremor { get; set; } = true;
    [Parameter, Setting("Leak — leaked corp memos", Group = "Effects", Order = 3)] public bool Leak { get; set; } = true;
    [Parameter, Setting("Schematic — geometry windows", Group = "Effects", Order = 4)] public bool Schematic { get; set; } = true;
    [Parameter, Setting("Cascade — console window bursts", Group = "Effects", Order = 5)] public bool Cascade { get; set; } = true;
    [Parameter, Setting("Artifact — floating glyph creatures", Group = "Effects", Order = 6)] public bool Artifact { get; set; } = true;
    [Parameter, Setting("Fragment — floating code fragments", Group = "Effects", Order = 7)] public bool Fragment { get; set; } = true;
    [Parameter, Setting("Trace — Tron-cycle network wires", Group = "Effects", Order = 8)] public bool Trace { get; set; } = true;
    [Parameter, Setting("Pulsar — Morse-code dots", Group = "Effects", Order = 9)] public bool Pulsar { get; set; } = true;
    [Parameter, Setting("Heist — folder-rip windows", Group = "Effects", Order = 10)] public bool Heist { get; set; } = true;
    [Parameter, Setting("Predator — artifact-hunting swarm", Group = "Effects", Order = 11)] public bool Predator { get; set; } = true;
    [Parameter, Setting("Terminal — console windows", Group = "Effects", Order = 12)] public bool Terminal { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        base.BuildRenderTree(builder);
        AddSettingsData(builder, 1000, "plugin.cyberspace");
    }
}
