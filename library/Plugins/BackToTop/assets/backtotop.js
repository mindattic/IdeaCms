/* IdeaCms.Plugin.BackToTop — creates the floating button and shows it after one screen of
 * scroll; click smooth-scrolls to the top. Safe to load more than once.
 * Instance settings (M.IDEA-§4.5) are read LIVE from <data data-ma-settings="plugin.backtotop" value="{json}">
 * (re-rendered on in-circuit navigation); every value falls back to the designed behavior. */
(function () {
  'use strict';
  if (window.__maBackToTop) return;
  window.__maBackToTop = true;

  var cache = { raw: null, parsed: {} };
  function settings() {
    var el = document.querySelector('[data-ma-settings="plugin.backtotop"]');
    var raw = el ? el.getAttribute('value') : null;
    if (raw !== cache.raw) {
      cache.raw = raw;
      try { cache.parsed = raw ? JSON.parse(raw) : {}; } catch (e) { cache.parsed = {}; }
    }
    return cache.parsed;
  }

  function start() {
    var b = document.createElement('button');
    b.type = 'button';
    b.className = 'ma-backtotop';
    b.setAttribute('aria-label', 'Back to top');
    b.addEventListener('click', function () {
      var s = settings();
      window.scrollTo({ top: 0, behavior: s.smoothScroll === false ? 'auto' : 'smooth' });
    });
    document.body.appendChild(b);

    function update() {
      var s = settings();
      var screens = typeof s.showAfterScreens === 'number' && s.showAfterScreens >= 0 ? s.showAfterScreens : 1;
      var label = s.label || 'Back to top';
      if (b.getAttribute('aria-label') !== label) { b.setAttribute('aria-label', label); b.title = s.label ? label : ''; }
      b.classList.toggle('ma-backtotop--no-anim', s.animations === false);
      b.classList.toggle('ma-backtotop-show', window.scrollY > window.innerHeight * screens);
    }
    window.addEventListener('scroll', update, { passive: true });
    window.addEventListener('resize', update);
    update();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
