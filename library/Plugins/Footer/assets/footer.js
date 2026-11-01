/* IdeaCms.Plugin.Footer — pin-when-short (the mindattic.com footer behavior).
 * While the document is shorter than the viewport the footer fixes to the bottom edge; as soon
 * as content grows past one screen it flows normally (the class comes off, so it can never
 * overlap content). Re-evaluated on resize and on DOM growth. Safe to load more than once.
 * Instance setting (M.IDEA-§4.5) "pinWhenShort" is read LIVE from
 * <data data-ma-settings="plugin.footer" value="{json}">; absent = pin (the designed behavior). */
(function () {
  'use strict';
  if (window.__maFooter) return;
  window.__maFooter = true;

  var cache = { raw: null, parsed: {} };
  function settings() {
    var el = document.querySelector('[data-ma-settings="plugin.footer"]');
    var raw = el ? el.getAttribute('value') : null;
    if (raw !== cache.raw) {
      cache.raw = raw;
      try { cache.parsed = raw ? JSON.parse(raw) : {}; } catch (e) { cache.parsed = {}; }
    }
    return cache.parsed;
  }

  function update() {
    var footer = document.querySelector('.ma-footer');
    if (!footer) return;
    // Measure with the pin off so a pinned footer doesn't hide its own height from the test.
    footer.classList.remove('ma-footer-pinned');
    if (settings().pinWhenShort === false) return;
    var short = document.documentElement.scrollHeight <= window.innerHeight;
    if (short) footer.classList.add('ma-footer-pinned');
  }

  function start() {
    update();
    window.addEventListener('resize', update);
    new MutationObserver(update).observe(document.body, { childList: true, subtree: true });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
