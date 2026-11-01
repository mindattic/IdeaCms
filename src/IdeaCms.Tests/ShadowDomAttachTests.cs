using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;
using IdeaCms.Library.Shared;

namespace IdeaCms.Tests;

/// <summary>
/// Shadow DOM opt-in (M.IDEA-§4.4): <see cref="MindAttic.Ideas.Abstractions.ComponentBase.UseShadowDom"/>
/// defaults to false (every existing Component citizen unaffected), and <see cref="ShadowDomAttach"/>
/// (the linked-source helper Component citizens opt into, same file library/Components/Textbox links)
/// only ever calls into JS for a component that opts in, only once interactive, and never lets a JS
/// failure surface -- M.IDEA-LAW-7.
/// </summary>
[TestFixture]
public class ShadowDomAttachTests
{
    private sealed class DefaultFlagComponent : MindAttic.Ideas.Abstractions.ComponentBase
    {
        public bool ReadUseShadowDom() => UseShadowDom;
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
    }

    [Test]
    public void UseShadowDom_DefaultsToFalse()
    {
        var component = new DefaultFlagComponent();
        Assert.That(component.ReadUseShadowDom(), Is.False);
    }

    /// <summary>A minimal Component citizen, standing in for a real one like Textbox: a stable root with
    /// unconditional children, opting in via the same recipe docs/AUTHORING.md documents.</summary>
    private sealed class ShadowOptInComponent : MindAttic.Ideas.Abstractions.ComponentBase
    {
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Parameter] public bool Shadow { get; set; }

        public static readonly string[] CssUrls = ["/test/component.css"];
        private ElementReference _root;

        protected override bool UseShadowDom => Shadow;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "ma-field");
            builder.AddElementReferenceCapture(2, r => _root = r);
            builder.AddContent(3, "hello");
            builder.CloseElement();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && UseShadowDom && RendererInfo.IsInteractive)
                await ShadowDomAttach.AttachAsync(JS, _root, CssUrls);
        }
    }

    [Test]
    public void OptedIn_Interactive_TriggersExactlyOneAttachCall_WithExpectedArgs()
    {
        using var ctx = new Bunit.TestContext();
        ctx.SetRendererInfo(new RendererInfo("Test", isInteractive: true));
        var module = ctx.JSInterop.SetupModule("./js/shadow-dom.js");
        module.Setup<bool>("attach", _ => true).SetResult(true);

        ctx.Render<ShadowOptInComponent>(p => p.Add(c => c.Shadow, true));

        var attachCalls = module.Invocations["attach"];
        Assert.That(attachCalls, Has.Count.EqualTo(1));
        Assert.That(attachCalls[0].Arguments, Has.Count.EqualTo(2));
        Assert.That(attachCalls[0].Arguments[1], Is.EqualTo(ShadowOptInComponent.CssUrls));
    }

    [Test]
    public void NotOptedIn_TriggersZeroJsInvocations()
    {
        using var ctx = new Bunit.TestContext();
        ctx.SetRendererInfo(new RendererInfo("Test", isInteractive: true));
        var module = ctx.JSInterop.SetupModule("./js/shadow-dom.js");
        module.Setup<bool>("attach", _ => true).SetResult(true);

        ctx.Render<ShadowOptInComponent>(p => p.Add(c => c.Shadow, false));

        Assert.That(module.Invocations.Count, Is.EqualTo(0));
    }

    [Test]
    public void NonInteractiveRender_SkipsTheInteropCallEntirely()
    {
        using var ctx = new Bunit.TestContext();
        ctx.SetRendererInfo(new RendererInfo("Test", isInteractive: false));
        var module = ctx.JSInterop.SetupModule("./js/shadow-dom.js");
        module.Setup<bool>("attach", _ => true).SetResult(true);

        ctx.Render<ShadowOptInComponent>(p => p.Add(c => c.Shadow, true));

        Assert.That(module.Invocations.Count, Is.EqualTo(0));
    }

    [Test]
    public void JsFailure_IsSwallowed_ComponentMarkupStaysIntact()
    {
        using var ctx = new Bunit.TestContext();
        ctx.SetRendererInfo(new RendererInfo("Test", isInteractive: true));
        var module = ctx.JSInterop.SetupModule("./js/shadow-dom.js");
        module.Setup<bool>("attach", _ => true).SetException(new JSException("boom"));

        IRenderedComponent<ShadowOptInComponent>? cut = null;
        Assert.DoesNotThrow(() => cut = ctx.Render<ShadowOptInComponent>(p => p.Add(c => c.Shadow, true)));
        Assert.That(cut!.Markup, Does.Contain("ma-field"));
    }
}
