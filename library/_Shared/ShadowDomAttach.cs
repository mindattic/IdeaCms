using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace IdeaCms.Library.Shared;

/// <summary>
/// Linked-source helper (NOT a project/package reference -- library/Directory.Build.props says the
/// ONLY reference every citizen may carry is the frozen Abstractions SDK, and the package validator
/// forbids host assemblies in a packed bin/) that wraps the JS interop call to attach a real Shadow DOM
/// shadow root to a Component's own root element. A component that opts in
/// (<c>protected override bool UseShadowDom => true;</c>) links this file directly into its own
/// assembly via <c>&lt;Compile Include="...\_Shared\ShadowDomAttach.cs" Link="ShadowDomAttach.cs" /&gt;</c>
/// in its own .csproj, alongside the compile-only <c>Microsoft.AspNetCore.Components.Web</c> reference
/// that already brings <c>Microsoft.JSInterop</c>'s types into scope (same pattern ModalPopup already
/// uses for <c>KeyboardEventArgs</c>). See docs/AUTHORING.md "Shadow DOM isolation".
/// </summary>
public static class ShadowDomAttach
{
    /// <summary>
    /// Moves <paramref name="host"/>'s already-rendered light-DOM children into a real open shadow root
    /// and adopts <paramref name="cssUrls"/> into it (see wwwroot/js/shadow-dom.js). Never throws -- any
    /// JS/interop failure is swallowed and false is returned, leaving the component exactly as it was:
    /// un-isolated, but still fully visible and correctly styled via whatever light-DOM stylesheet
    /// reference it already renders unconditionally (M.IDEA-LAW-7 -- a page must never be invalid).
    /// </summary>
    public static async Task<bool> AttachAsync(IJSRuntime js, ElementReference host, IReadOnlyList<string> cssUrls)
    {
        try
        {
            await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/shadow-dom.js");
            return await module.InvokeAsync<bool>("attach", host, cssUrls);
        }
        catch (JSException)
        {
            return false;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
