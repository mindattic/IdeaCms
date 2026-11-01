// ES module (imported via IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/shadow-dom.js")),
// not a global IIFE like admin-monaco.js -- this is shared CMS-wide infrastructure an open set of
// independently-authored Component packages call, not one bespoke admin screen, so a window-scoped
// global name would be a real collision risk. See docs/AUTHORING.md "Shadow DOM isolation".

// Module-scoped cache: fetch + parse each stylesheet URL once, share the resulting CSSStyleSheet across
// every shadow root that adopts it (mirrors PageAssetCollector's per-type CSS dedup philosophy).
const sheetCache = new Map();

async function loadStyleSheet(url) {
    if (!sheetCache.has(url)) {
        sheetCache.set(url, (async () => {
            const res = await fetch(url);
            const text = await res.text();
            const sheet = new CSSStyleSheet();
            sheet.replaceSync(text);
            return sheet;
        })());
    }
    return sheetCache.get(url);
}

/**
 * Attaches an open shadow root to `host`, moving its already-rendered light-DOM children into it (never
 * cloning -- cloning would break Blazor Server's event-dispatch, which is keyed to live Node identity)
 * and adopting the given stylesheet URLs. Idempotent: a second call on an already-shadowed host is a
 * no-op success. Never throws across the interop boundary -- any failure returns false so the caller
 * (ShadowDomAttach.cs) can swallow it and leave the component in its already-correct, un-isolated state.
 *
 * @param {HTMLElement} host
 * @param {string[]} cssUrls
 * @returns {Promise<boolean>}
 */
export async function attach(host, cssUrls) {
    try {
        if (!host || typeof host.attachShadow !== "function") return false;
        if (host.shadowRoot) return true; // already attached -- idempotent.

        const shadow = host.attachShadow({ mode: "open" });

        // Move (not clone) every existing light-DOM child into the new shadow root, preserving
        // Blazor's event listeners/state, which are bound to the actual Node objects.
        while (host.firstChild) shadow.appendChild(host.firstChild);

        if (Array.isArray(cssUrls) && cssUrls.length > 0) {
            if ("adoptedStyleSheets" in shadow && typeof CSSStyleSheet === "function") {
                try {
                    const sheets = await Promise.all(cssUrls.map(loadStyleSheet));
                    shadow.adoptedStyleSheets = sheets;
                } catch {
                    // adoptedStyleSheets construction failed (e.g. fetch blocked) -- fall back to <link>.
                    for (const url of cssUrls) {
                        const link = document.createElement("link");
                        link.rel = "stylesheet";
                        link.href = url;
                        shadow.appendChild(link);
                    }
                }
            } else {
                for (const url of cssUrls) {
                    const link = document.createElement("link");
                    link.rel = "stylesheet";
                    link.href = url;
                    shadow.appendChild(link);
                }
            }
        }

        return true;
    } catch {
        return false;
    }
}
