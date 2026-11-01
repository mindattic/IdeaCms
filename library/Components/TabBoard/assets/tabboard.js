/* IdeaCms.Component.TabBoard — the mindattic.com tab-board engine, verbatim from index.htm.
 *
 * Drop the token once and:
 *   - authored .board-section / .board-grid / .tabButton[data-target] / .tabPage markup is wired:
 *     inline panels lift into a .tabButton-tabpanels sibling, placeholder tiles get stable
 *     procedural art, each section gets a stable localStorage key, the last-open tab restores,
 *     and one document-level click handler runs the single-active-tab behavior.
 *   - window.TabBoard exposes the engine for page scripts that build boards from data:
 *       TabBoard.build(items)  -> a .home-sections board element (buildBoardSection verbatim)
 *       TabBoard.art(name)     -> stable procedural tile art (generateProjectArt verbatim)
 *       TabBoard.images        -> name->src registry consulted before generating art
 *       TabBoard.refresh()     -> re-wire after the DOM changes (also runs automatically)
 *
 * Placement options (set by the include tag, e.g. <Component.TabBoard alwaysShowTabPage="true" />;
 * the widget emits them onto window.TabBoardConfig):
 *   alwaysShowTabPage — every board keeps a selected item visible: clicking the open tab no longer
 *                       collapses it, and a board with no saved selection opens its first tab.
 *                       Selection still persists per section in localStorage either way.
 *
 * Verbatim except: the empty PROJECT_IMAGES map became the TabBoard.images registry, and the two
 * alwaysShowTabPage hooks marked CONFIG below.
 * Safe to load more than once. No dependencies, no external requests. */
(function () {
  'use strict';
  if (window.TabBoard) return;

  var images = {};

  // CONFIG: read lazily so it works no matter which order the engine and the token's inline
  // config script executed in.
  function isAlwaysShow() {
    return !!(window.TabBoardConfig && window.TabBoardConfig.alwaysShowTabPage);
  }
  // CONFIG (M.IDEA-§4.5 instance settings): each falls back to the verbatim behavior when unset.
  function cfg(name, fallback) {
    var c = window.TabBoardConfig;
    return c && c[name] !== undefined && c[name] !== null ? c[name] : fallback;
  }

    var PROJECT_ART_PALETTES = [
      ['#0f2027', '#2c5364', '#71f0c8'],
      ['#3a1c71', '#d76d77', '#ffaf7b'],
      ['#1a2980', '#26d0ce', '#ffffff'],
      ['#232526', '#414345', '#f59f00'],
      ['#000428', '#004e92', '#7afcff'],
      ['#0f0c29', '#302b63', '#ff6b6b'],
      ['#134e5e', '#71b280', '#fff700'],
      ['#43cea2', '#185a9d', '#ffffff'],
      ['#5b247a', '#1bcedf', '#ffe66d'],
      ['#373b44', '#4286f4', '#80f8ff'],
      ['#2c003e', '#7700ff', '#ffd166'],
      ['#1f4037', '#99f2c8', '#ffeaa7'],
      ['#1d2671', '#c33764', '#f7d8ba'],
      ['#08313a', '#2980b9', '#f1c40f'],
      ['#093028', '#237a57', '#fffae5'],
      ['#360033', '#0b8793', '#a0ffe6']
    ];

    // Deterministic abstract art per project. The same name always yields the
    // same SVG, so each tile gets a stable "fingerprint" image. Returns a
    // data: URI (base64-encoded SVG) suitable for <img src=...>.
    function generateProjectArt(name) {
      var seed = 2166136261 >>> 0;
      for (var i = 0; i < name.length; i++) {
        seed ^= name.charCodeAt(i);
        seed = Math.imul(seed, 16777619) >>> 0;
      }
      function rand() {
        seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
        return seed / 4294967296;
      }
      var p = PROJECT_ART_PALETTES[Math.floor(rand() * PROJECT_ART_PALETTES.length)];
      var c1 = p[0], c2 = p[1], accent = p[2];

      // Gradient direction
      var ang = rand() * Math.PI * 2;
      var dx = Math.cos(ang) * 50, dy = Math.sin(ang) * 50;
      var x1 = (50 - dx).toFixed(0) + '%', y1 = (50 - dy).toFixed(0) + '%';
      var x2 = (50 + dx).toFixed(0) + '%', y2 = (50 + dy).toFixed(0) + '%';

      var shapes = '';
      var count = 5 + Math.floor(rand() * 4);
      for (var s = 0; s < count; s++) {
        var sx = (rand() * 360 - 30).toFixed(0);
        var sy = (rand() * 360 - 30).toFixed(0);
        var col = [c1, c2, accent][Math.floor(rand() * 3)];
        var op = (rand() * 0.45 + 0.18).toFixed(2);
        var kind = Math.floor(rand() * 3);
        if (kind === 0) {
          var rr = (rand() * 90 + 25).toFixed(0);
          shapes += '<circle cx="' + sx + '" cy="' + sy + '" r="' + rr + '" fill="' + col + '" opacity="' + op + '"/>';
        } else if (kind === 1) {
          var w = (rand() * 160 + 50).toFixed(0);
          var h = (rand() * 160 + 50).toFixed(0);
          var rot = Math.floor(rand() * 360);
          shapes += '<rect x="' + sx + '" y="' + sy + '" width="' + w + '" height="' + h + '" fill="' + col + '" opacity="' + op + '" transform="rotate(' + rot + ' ' + sx + ' ' + sy + ')"/>';
        } else {
          var p1x = (parseFloat(sx) + (rand() - 0.5) * 220).toFixed(0);
          var p1y = (parseFloat(sy) + (rand() - 0.5) * 220).toFixed(0);
          var p2x = (parseFloat(sx) + (rand() - 0.5) * 220).toFixed(0);
          var p2y = (parseFloat(sy) + (rand() - 0.5) * 220).toFixed(0);
          shapes += '<polygon points="' + sx + ',' + sy + ' ' + p1x + ',' + p1y + ' ' + p2x + ',' + p2y + '" fill="' + col + '" opacity="' + op + '"/>';
        }
      }

      var letter = (name.match(/[A-Za-z0-9]/) || ['?'])[0].toUpperCase();
      var svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 300" preserveAspectRatio="xMidYMid slice">' +
        '<defs><linearGradient id="g" x1="' + x1 + '" y1="' + y1 + '" x2="' + x2 + '" y2="' + y2 + '">' +
        '<stop offset="0" stop-color="' + c1 + '"/><stop offset="1" stop-color="' + c2 + '"/>' +
        '</linearGradient></defs>' +
        '<rect width="300" height="300" fill="url(#g)"/>' +
        shapes +
        '<text x="150" y="190" font-family="\'Outfit\',system-ui,sans-serif" font-size="130" font-weight="800" fill="' + accent + '" fill-opacity="0.92" text-anchor="middle">' + letter + '</text>' +
        '</svg>';
      return 'data:image/svg+xml;base64,' + btoa(svg);
    }

    // After the authored Software/Hardware markup is in the DOM, walk every
    // placeholder image div and swap it for either an explicit PROJECT_IMAGES
    // entry or a generated abstract SVG keyed by the tile's display name.
    function hydratePlaceholderImages() {
      var placeholders = document.querySelectorAll('.tabPage-img--placeholder');
      for (var i = 0; i < placeholders.length; i++) {
        var el = placeholders[i];
        var panel = el.closest ? el.closest('.tabPage') : null;
        if (!panel) continue;
        var name = '';
        var repo = panel.getAttribute('data-repo');
        if (repo) {
          name = repo.split('/').pop();
        } else if (panel.id) {
          var btn = document.querySelector('button[data-target="' + CSS.escape(panel.id) + '"] .tabButton-name');
          if (btn) name = btn.textContent.trim();
        }
        if (!name) continue;
        if (!images[name] && !cfg('generateArt', true)) continue;
        var src = images[name] || generateProjectArt(name);
        var img = document.createElement('img');
        img.src = src;
        img.alt = name;
        el.classList.remove('tabPage-img--placeholder');
        el.appendChild(img);
      }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-tile button URLs. Each map is keyed by the tile's display name
    // (Portfolio link text, book title, or visual-art piece title) and
    // returns the URL the button on that tile should open. Editing these
    // tables is how you change destination links without touching markup.
    //
    //   Portfolio button label:   "Open"
    //   Software Dev button label: "GitHub"  (driven by gh repo list — see
    //                                          fetch-descriptions.ps1)
    //   Writing button label:     "Amazon"
    //   Visual Arts button label: "Open"
    //
    // If a name is not in its map, the JS falls back to the original
    // <a href="..."> on the page.
    // ─────────────────────────────────────────────────────────────────────
    function buildBoardSection(items) {
      var home = document.createElement('div'); home.className = 'home-sections';
      var sec  = document.createElement('div'); sec.className  = 'board-section';
      var grid = document.createElement('div'); grid.className = 'board-grid';
      var panels = document.createElement('div'); panels.className = 'tabButton-tabpanels';

      items.forEach(function (it) {
        var tabButton = document.createElement('button');
        tabButton.type = 'button';
        tabButton.className = 'tabButton';
        tabButton.setAttribute('data-target', it.id);
        var name = document.createElement('div');
        name.className = 'tabButton-name';
        name.textContent = it.name;
        tabButton.appendChild(name);
        grid.appendChild(tabButton);

        var tabPage = document.createElement('div');
        tabPage.className = 'tabPage';
        tabPage.id = it.id;

        // Row 1: image (left) + body (right).
        var row = document.createElement('div');
        row.className = 'tabPage-row';

        if (it.img) {
          var imgWrap = document.createElement('div');
          imgWrap.className = 'tabPage-img' + (it.imgClass ? (' ' + it.imgClass) : '');
          var img = document.createElement('img');
          img.src = it.img; img.alt = it.name;
          imgWrap.appendChild(img);
          if (it.isSnapshot) {
            imgWrap.classList.add('web-snapshot');
          }
          row.appendChild(imgWrap);
        } else if (it.monogram) {
          var mono = document.createElement('div');
          mono.className = 'tabPage-mono';
          mono.textContent = it.monogram;
          row.appendChild(mono);
        }

        var body = document.createElement('div');
        body.className = 'tabPage-body';
        var h = document.createElement('h3'); h.textContent = it.name; body.appendChild(h);
        if (it.body) { var p = document.createElement('p'); p.textContent = it.body; body.appendChild(p); }
        row.appendChild(body);
        tabPage.appendChild(row);

        // Row 2: button strip (sibling of the image/body row).
        if (it.href) {
          var links = document.createElement('div');
          links.className = 'tabPage-links';
          var a = document.createElement('a');
          a.className = 'tabButton-btn';
          a.href = it.href;
          if (cfg('linkNewTab', true)) a.target = '_blank';
          a.rel = 'noopener noreferrer';
          a.textContent = it.linkLabel || cfg('linkLabel', 'Open');
          links.appendChild(a);
          tabPage.appendChild(links);
        }
        panels.appendChild(tabPage);
      });

      sec.appendChild(grid);
      sec.appendChild(panels);
      home.appendChild(sec);
      return home;
    }

    // ─── Lift any interleaved .tabPage out of its .board-grid ─────
    function liftPanels() {
      var grids = document.querySelectorAll('.board-grid');
      for (var g = 0; g < grids.length; g++) {
        var grid = grids[g];
        var inline = grid.querySelectorAll('.tabPage');
        if (!inline.length) continue;
        var section = grid.parentNode;
        var panels = section.querySelector('.tabButton-tabpanels');
        if (!panels) {
          panels = document.createElement('div');
          panels.className = 'tabButton-tabpanels';
          section.appendChild(panels);
        }
        for (var i = 0; i < inline.length; i++) panels.appendChild(inline[i]);
      }
    }

    // ─── Assign each .board-section a stable key (preceding <h2>, with a
    //     numeric suffix if multiple sections share the same heading) ──────
    function buildSectionKeys() {
      var nodes = document.querySelectorAll('h2, .board-section');
      var currentH2 = null;
      var counters = {};
      for (var i = 0; i < nodes.length; i++) {
        var node = nodes[i];
        if (node.tagName === 'H2') {
          currentH2 = node.textContent.trim();
          counters[currentH2] = 0;
        } else {
          var label = currentH2 || 'unnamed';
          counters[label] = (counters[label] || 0) + 1;
          var suffix = counters[label] > 1 ? (':' + counters[label]) : '';
          node.setAttribute('data-section-key', 'mindattic.tab:' + label + suffix);
        }
      }
    }

    // ─── Restore the last-opened tab in each section from localStorage.
    //     Stored value is the tab's data-target id; empty string means the
    //     user explicitly closed it last time, so stay collapsed. Missing
    //     key (first-time visitor) → also stay collapsed.
    function restoreSelection() {
      var sections = document.querySelectorAll('.board-section');
      for (var s = 0; s < sections.length; s++) {
        var section = sections[s];
        var key = section.getAttribute('data-section-key');
        if (!key || !cfg('persist', true)) continue;
        var saved = null;
        try { saved = localStorage.getItem(key); } catch (e) {}
        if (!saved) continue;
        var target = section.querySelector('.tabButton[data-target="' + saved + '"]');
        if (!target) continue;
        var page = document.getElementById(saved);
        if (!page) continue;
        target.classList.add('is-open');
        page.classList.add('is-open');
      }
    }

    // ─── Single-active tab click handler, scoped per .board-section ───────
    //     All tabs start collapsed; clicking the active tab closes it.
    //     Selection is persisted per section in localStorage — empty string
    //     means "user closed it, stay collapsed on next load".
    function wireClicks() {
      document.addEventListener('click', function (e) {
        var tabButton = e.target.closest('.tabButton[data-target]');
        if (!tabButton) return;
        var id = tabButton.getAttribute('data-target');
        var tabPage = document.getElementById(id);
        if (!tabPage) return;
        var section = tabButton.closest('.board-section') || document;
        var wasOpen = tabButton.classList.contains('is-open');
        // CONFIG: under alwaysShowTabPage a selected item stays visible — re-clicking it is a no-op.
        if (wasOpen && isAlwaysShow()) return;
        var openTabButtons = section.querySelectorAll('.tabButton.is-open');
        var openTabPages = section.querySelectorAll('.tabPage.is-open');
        for (var i = 0; i < openTabButtons.length; i++) openTabButtons[i].classList.remove('is-open');
        for (var j = 0; j < openTabPages.length; j++) openTabPages[j].classList.remove('is-open');
        if (!wasOpen) {
          tabButton.classList.add('is-open');
          tabPage.classList.add('is-open');
        }
        var key = section.getAttribute && section.getAttribute('data-section-key');
        if (key && cfg('persist', true)) {
          try { localStorage.setItem(key, wasOpen ? '' : id); } catch (e) {}
        }
      });
    }

    // Defer until the entire body has parsed — the .books-grid and other
    // late-DOM sections appear after this inline <script> in source order.

  // CONFIG: under alwaysShowTabPage a board never sits fully collapsed — after restoreSelection
  // (saved id wins) any section with nothing open opens its FIRST tab.
  function enforceAlwaysOpen() {
    if (!isAlwaysShow()) return;
    var sections = document.querySelectorAll('.board-section');
    for (var s = 0; s < sections.length; s++) {
      var section = sections[s];
      if (section.querySelector('.tabButton.is-open')) continue;
      var first = section.querySelector('.tabButton[data-target]');
      if (!first) continue;
      var page = document.getElementById(first.getAttribute('data-target'));
      if (!page) continue;
      first.classList.add('is-open');
      page.classList.add('is-open');
    }
  }

  // ── exposure + auto-init (idempotent; re-runs when the host swaps/extends the DOM) ──
  var clicksWired = false;
  var scheduled = false;
  function refresh() {
    liftPanels();
    hydratePlaceholderImages();
    buildSectionKeys();
    restoreSelection();
    enforceAlwaysOpen();
    if (!clicksWired) { clicksWired = true; wireClicks(); }
  }
  function scheduleRefresh() {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(function () { scheduled = false; refresh(); });
  }
  window.TabBoard = { build: buildBoardSection, art: generateProjectArt, images: images, refresh: refresh };
  function start() {
    refresh();
    new MutationObserver(scheduleRefresh).observe(document.body, { childList: true, subtree: true });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
