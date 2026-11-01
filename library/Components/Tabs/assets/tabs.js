/* IdeaCms.Component.Tabs — wires any .ma-tabs container into an accessible tab control.
 *
 * Author markup (content only; the tablist is GENERATED from data-title):
 *   <div class="ma-tabs">                      <!-- add ma-tabs-board for the tile-board look -->
 *     <section data-title="One">…</section>
 *     <section data-title="Two">…</section>
 *   </div>
 *
 * Behavior: first tab opens by default (or none, with data-closed on the container — the
 * tab-board pattern); click toggles; Left/Right/Home/End move focus per the ARIA tabs pattern.
 * A MutationObserver wires containers rendered at any time (Blazor enhanced nav, late includes).
 * Safe to load more than once. No dependencies, no external requests.
 */
(function () {
  'use strict';
  if (window.__maTabs) return;
  window.__maTabs = true;

  var uid = 0;

  // Instance settings (M.IDEA-§4.5) from the token's inline script; each falls back to the default behavior.
  function cfg(name, fallback) {
    var c = window.MaTabsConfig;
    return c && c[name] !== undefined && c[name] !== null ? c[name] : fallback;
  }

  function wire(root) {
    if (root.__maTabsWired) return;
    root.__maTabsWired = true;

    var panels = Array.prototype.filter.call(root.children, function (el) {
      return el.hasAttribute('data-title');
    });
    if (!panels.length) return;

    var list = document.createElement('div');
    list.className = 'ma-tabs-list';
    list.setAttribute('role', 'tablist');

    var tabs = panels.map(function (panel, i) {
      var id = 'ma-tab-' + (++uid);
      var tab = document.createElement('button');
      tab.type = 'button';
      tab.className = 'ma-tabs-tab';
      tab.id = id;
      tab.setAttribute('role', 'tab');
      tab.textContent = panel.getAttribute('data-title');
      panel.classList.add('ma-tabs-panel');
      panel.setAttribute('role', 'tabpanel');
      panel.setAttribute('aria-labelledby', id);
      tab.addEventListener('click', function () {
        var isOpen = tab.getAttribute('aria-selected') === 'true';
        select(isOpen && allowClosed ? -1 : i);
      });
      if (cfg('keyboard', true)) tab.addEventListener('keydown', function (e) {
        var next = { ArrowRight: i + 1, ArrowLeft: i - 1, Home: 0, End: tabs.length - 1 }[e.key];
        if (next === undefined) return;
        e.preventDefault();
        next = (next + tabs.length) % tabs.length;
        tabs[next].focus();
        select(next);
      });
      list.appendChild(tab);
      return tab;
    });

    var allowClosed = root.hasAttribute('data-closed') || !!cfg('startClosed', false);

    function select(active) {
      tabs.forEach(function (tab, i) {
        var on = i === active;
        tab.setAttribute('aria-selected', on ? 'true' : 'false');
        tab.tabIndex = on || (active === -1 && i === 0) ? 0 : -1;
        panels[i].hidden = !on;
      });
    }

    root.insertBefore(list, root.firstChild);
    select(allowClosed ? -1 : 0);
  }

  function wireAll(scope) {
    Array.prototype.forEach.call(scope.querySelectorAll('.ma-tabs'), wire);
  }

  function start() {
    wireAll(document);
    new MutationObserver(function (muts) {
      muts.forEach(function (m) {
        Array.prototype.forEach.call(m.addedNodes, function (n) {
          if (n.nodeType !== 1) return;
          if (n.classList && n.classList.contains('ma-tabs')) wire(n);
          else if (n.querySelectorAll) wireAll(n);
        });
      });
    }).observe(document.body, { childList: true, subtree: true });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
