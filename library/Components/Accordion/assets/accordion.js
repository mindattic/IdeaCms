/* IdeaCms.Component.Accordion — exclusive-open behavior for .ma-accordion[data-exclusive]
 * (open/close itself is native <details>; this only closes the siblings). Event delegation, so
 * accordions rendered at any time work. Safe to load more than once.
 * Instance setting exclusiveByDefault (data-ma-settings="component.accordion") makes every
 * accordion exclusive; data-exclusive="false" on a container opts it back out. */
(function () {
  'use strict';
  if (window.__maAccordion) return;
  window.__maAccordion = true;

  function exclusiveByDefault() {
    var el = document.querySelector('[data-ma-settings="component.accordion"]');
    if (!el) return false;
    try { return !!JSON.parse(el.getAttribute('value') || '{}').exclusiveByDefault; } catch (e) { return false; }
  }

  document.addEventListener('toggle', function (e) {
    var details = e.target;
    if (!(details instanceof HTMLElement) || !details.open) return;
    var all = exclusiveByDefault();
    var group = details.closest(all ? '.ma-accordion' : '.ma-accordion[data-exclusive]');
    if (!group) return;
    if (all && group.getAttribute('data-exclusive') === 'false') return;
    Array.prototype.forEach.call(group.querySelectorAll(':scope > details[open]'), function (d) {
      if (d !== details) d.open = false;
    });
  }, true);  // toggle doesn't bubble; capture it.
})();
