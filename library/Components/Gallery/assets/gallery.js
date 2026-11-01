/* IdeaCms.Component.Gallery — lightbox for UNLINKED tiles inside .ma-gallery (anchor tiles
 * navigate normally — the books-grid case needs no JS at all). A tile's image is wherever the page
 * put it: an <img src="data:…"> or, per the page convention, a base64 background-image CSS class —
 * the lightbox reads the computed background. One overlay is created and reused; Esc closes,
 * arrows step through the unlinked tiles of the SAME gallery. Event delegation, so galleries
 * rendered at any time work. Safe to load more than once. No external requests. */
(function () {
  'use strict';
  if (window.__maGallery) return;
  window.__maGallery = true;

  var box, img, items = [], index = -1;

  // Instance settings (M.IDEA-§4.5) published by the Gallery component as
  // <data data-ma-settings="component.gallery" value="{json}">. Read live so in-circuit navigation
  // picks up the current page's values; every missing key falls back to the as-designed behaviour.
  function settings() {
    var el = document.querySelector('[data-ma-settings="component.gallery"]');
    var s = {};
    if (el) { try { s = JSON.parse(el.value || el.getAttribute('value') || '{}') || {}; } catch (e) { s = {}; } }
    function flag(k) { return s[k] !== false; }
    return {
      lightbox: flag('lightbox'), arrows: flag('showArrows'), keyboard: flag('keyboard'),
      loop: flag('loop'), backdrop: flag('closeOnBackdrop')
    };
  }

  // The tile's image, however it was authored: <img src> or a background-image class.
  function urlOf(el) {
    if (el instanceof HTMLImageElement) return el.src;
    var inner = el.querySelector && el.querySelector('img');
    if (inner) return inner.src;
    var bg = getComputedStyle(el).backgroundImage;
    var m = bg && bg.match(/url\(["']?(.+?)["']?\)/);
    return m ? m[1] : null;
  }

  function altOf(el) {
    return (el instanceof HTMLImageElement && el.alt) || el.getAttribute('aria-label') || '';
  }

  function build() {
    if (box) return;
    box = document.createElement('div');
    box.className = 'ma-gallery-lightbox';
    box.hidden = true;
    img = document.createElement('img');
    var close = btn('ma-gallery-close', '×', hide);
    var prev = btn('ma-gallery-prev', '‹', function () { step(-1); });
    var next = btn('ma-gallery-next', '›', function () { step(1); });
    box.appendChild(img); box.appendChild(close); box.appendChild(prev); box.appendChild(next);
    box.addEventListener('click', function (e) { if (e.target === box && settings().backdrop) hide(); });
    document.body.appendChild(box);
  }

  function btn(cls, text, fn) {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = cls;
    b.textContent = text;
    b.setAttribute('aria-label', cls.replace('ma-gallery-', ''));
    b.addEventListener('click', fn);
    return b;
  }

  function showAt(i) {
    if (i < 0 || !items.length) return;
    index = i;
    img.src = items[i].url;
    img.alt = items[i].alt;
    var arrows = settings().arrows && items.length > 1;
    box.querySelector('.ma-gallery-prev').hidden = !arrows;
    box.querySelector('.ma-gallery-next').hidden = !arrows;
    box.hidden = false;
  }

  function step(dir) {
    if (box.hidden || !items.length) return;
    var next = index + dir;
    if (!settings().loop && (next < 0 || next >= items.length)) return;
    showAt((next + items.length) % items.length);
  }

  function hide() { if (box) box.hidden = true; }

  document.addEventListener('click', function (e) {
    if (!(e.target instanceof Element) || e.target.closest('a')) return;
    var gallery = e.target.closest('.ma-gallery');
    if (!gallery || !settings().lightbox) return;
    var tile = e.target.closest('.ma-gallery > *');
    if (!tile) return;
    var url = urlOf(tile);
    if (!url) return;
    build();
    // Collect this gallery's unlinked, imaged tiles so prev/next stay within it.
    items = Array.prototype.map.call(gallery.children, function (el) {
      return el.tagName === 'A' || el.closest('a') ? null
           : (function (u) { return u ? { url: u, alt: altOf(el) } : null; })(urlOf(el));
    }).filter(Boolean);
    showAt(items.findIndex(function (it) { return it.url === url; }));
  });

  document.addEventListener('keydown', function (e) {
    if (!box || box.hidden || !settings().keyboard) return;
    if (e.key === 'Escape') hide();
    else if (e.key === 'ArrowLeft') step(-1);
    else if (e.key === 'ArrowRight') step(1);
  });
})();
