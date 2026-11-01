#pragma warning disable BL0006 // RenderTree types: inspecting frame shape is the point of this test.
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;
using IdeaCms.Rendering;

namespace IdeaCms.Tests;

/// <summary>
/// Regression: a &lt;style&gt;/&lt;script&gt; with raw content must be emitted as ONE markup frame, never as an
/// element frame with a markup child. Interactive Blazor inserts a markup child through a &lt;template&gt; innerHTML parse
/// outside raw-text context, so a "&lt;main&gt;" inside a CSS comment became a real element and every rule
/// after it silently left the stylesheet — the frontpage lost its grid ~0.5s after load, once the circuit
/// connected. Prerendered HTML is byte-identical either way, so only the frame shape can catch this.
/// </summary>
[TestFixture]
public class RawTextElementFrameTests
{
    private sealed class CapturingRenderer(IServiceProvider sp) : Renderer(sp, NullLoggerFactory.Instance)
    {
        public List<RenderTreeFrame> Frames { get; } = new();
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => throw exception;

        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            for (var i = 0; i < batch.ReferenceFrames.Count; i++)
                Frames.Add(batch.ReferenceFrames.Array[i]);
            return Task.CompletedTask;
        }

        public Task RenderAsync<T>(ParameterView parameters) where T : IComponent =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(AssignRootComponentId(InstantiateComponent(typeof(T))), parameters));
    }

    private sealed class CascadeHost : Microsoft.AspNetCore.Components.ComponentBase
    {
        [Parameter] public IRenderContext Ctx { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenComponent<CascadingValue<IRenderContext>>(0);
            b.AddComponentParameter(1, "Value", Ctx);
            b.AddComponentParameter(2, "IsFixed", true);
            b.AddComponentParameter(3, "ChildContent", (RenderFragment)(cb =>
            {
                cb.OpenComponent<FreeFormPage>(0);
                cb.CloseComponent();
            }));
            b.CloseComponent();
        }
    }

    private static void AssertNoRawTextElementWithContentChild(List<RenderTreeFrame> frames)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            if (f.FrameType != RenderTreeFrameType.Element || f.ElementName is not ("style" or "script")) continue;
            for (var j = i + 1; j < i + f.ElementSubtreeLength; j++)
                // Text children are safe (inserted as text nodes); only markup children get re-parsed.
                Assert.That(frames[j].FrameType, Is.Not.EqualTo(RenderTreeFrameType.Markup),
                    $"<{f.ElementName}> has a markup child frame; emit the whole element as one markup string");
        }
    }

    private const string CssWithTagsInComments =
        "/* styles every region inside <main>: */ #content{max-width:800px} /* <img class=\"x\"> */ .grid{display:grid}";

    [Test]
    public async Task FreeFormPage_EmitsStyleAndScriptAsSingleMarkupFrames()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IContentCatalog>(new ContentCatalog(new DefaultTypeResolver()));
        services.AddSingleton<IRawContentGate, RawContentGate>();
        services.AddSingleton<IRenderAlertSink, NullRenderAlertSink>();
        await using var provider = services.BuildServiceProvider();

        var ctx = new CmsRenderContext
        {
            InstanceId = Guid.NewGuid(),
            Mode = ContentMode.View,
            RenderMode = CmsRenderMode.Static,
            Page = new CmsPageContext
            {
                PageId = Guid.NewGuid(), Slug = "styled", Title = "Styled",
                Inline = new CmsInlineMarkup { Css = CssWithTagsInComments, Html = "<p>hi</p>", Js = "if (1 < 2) { document.title = '<b>'; }", Trusted = true },
            },
            Site = new CmsSiteContext { SiteId = Guid.Empty, Key = "default", Host = "localhost", DefaultThemeKey = "" },
            Services = provider,
        };

        var renderer = new CapturingRenderer(provider);
        await renderer.RenderAsync<CascadeHost>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Ctx"] = ctx }));

        AssertNoRawTextElementWithContentChild(renderer.Frames);
        Assert.That(renderer.Frames.Any(f => f.FrameType == RenderTreeFrameType.Markup
            && f.MarkupContent.StartsWith("<style>@layer page {") && f.MarkupContent.Contains(".grid{display:grid}")));
    }

    [Test]
    public async Task CmsHead_EmitsStyleBlocksAsSingleMarkupFrames()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var renderer = new CapturingRenderer(provider);
        await renderer.RenderAsync<CmsHead>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["GlobalCss"] = CssWithTagsInComments,
            ["ThemeCss"] = new List<string> { "b.css" },
        }));

        AssertNoRawTextElementWithContentChild(renderer.Frames);
    }
}
