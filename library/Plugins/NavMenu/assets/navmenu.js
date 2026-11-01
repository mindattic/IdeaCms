/* IdeaCms.Plugin.NavMenu — hamburger toggle for the mobile breakpoint.
 * Event delegation on document so menus rendered at any time (Blazor enhanced nav,
 * late includes) work without per-element wiring. Safe to load more than once. */
(function () {
  'use strict';
  if (window.__maNavMenu) return;
  window.__maNavMenu = true;

  document.addEventListener('click', function (e) {
    var toggle = e.target.closest('.ma-header-toggle');
    if (!toggle) return;
    var nav = toggle.closest('.ma-header');
    if (!nav) return;
    var open = nav.classList.toggle('open');
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
  });
})();
