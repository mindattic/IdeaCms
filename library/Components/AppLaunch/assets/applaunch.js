/* IdeaCms.Component.AppLaunch — launch a hosted app borderless.
 *
 * Event delegation on document, so a tile rendered at any time (Blazor re-render, enhanced nav,
 * a late include) works with no per-element wiring. Safe to load more than once.
 *
 * THE BROWSER RULE THIS IS BUILT AROUND
 * ------------------------------------
 * A page cannot put a window it opened into fullscreen. Calling requestFullscreen() on a popup's
 * document from the opener is rejected ("Permissions check failed") even when the two are
 * same-origin — the activating gesture has to happen INSIDE the target window. So there is no
 * single call that yields "separate borderless fullscreen window"; there is a ladder:
 *
 *   mode="fullscreen"  overlay iframe in THIS window + Fullscreen API   -> zero browser chrome,
 *                      one click, no permission prompt. The reliable one.
 *   mode="window"      window.open(url + "?ma-fs=1", popup) -> a real separate window. The opened
 *                      page arms itself (see armFromQueryString) and goes borderless on its own
 *                      first gesture. Requires the opened page to carry THIS script — i.e. it is
 *                      an Ideas page with an AppLaunch on it. Popup blocked -> falls back to the
 *                      overlay rung.
 *   mode="inline"      embedded iframe, no launch step, with a fullscreen affordance.
 *
 * Rung below all of them: if requestFullscreen is missing or rejects, the overlay stays a
 * position:fixed cover — borderless within the tab rather than borderless on the screen.
 */
(function () {
  'use strict';
  if (window.__maAppLaunch) return;
  window.__maAppLaunch = true;

  var FS_PARAM = 'ma-fs';
  var FRAME_ALLOW = 'fullscreen; autoplay; gamepad; clipboard-write; xr-spatial-tracking';

  function attr(el, name, dflt) {
    var v = el.getAttribute(name);
    return (v === null || v === '') ? dflt : v;
  }

  function requestFs(el) {
    var req = el.requestFullscreen || el.webkitRequestFullscreen || el.msRequestFullscreen;
    if (!req) return Promise.reject(new Error('no Fullscreen API'));
    try {
      var p = req.call(el);
      return (p && p.then) ? p : Promise.resolve();
    } catch (e) {
      return Promise.reject(e);
    }
  }

  function exitFs() {
    if (!document.fullscreenElement && !document.webkitFullscreenElement) return;
    var ex = document.exitFullscreen || document.webkitExitFullscreen;
    if (ex) { try { ex.call(document); } catch (e) { /* already gone */ } }
  }

  function buildFrame(cfg) {
    var frame = document.createElement('iframe');
    frame.className = 'ma-applaunch-frame';
    frame.src = cfg.url;
    frame.title = cfg.title || 'Application';
    frame.setAttribute('allowfullscreen', '');
    frame.setAttribute('allow', FRAME_ALLOW);
    return frame;
  }

  /* ---- rung 1/2: overlay in this window ------------------------------------------------- */

  function openOverlay(cfg) {
    if (document.querySelector('.ma-applaunch-overlay')) return;   // one at a time

    var overlay = document.createElement('div');
    overlay.className = 'ma-applaunch-overlay';
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', cfg.title || 'Application');
    overlay.tabIndex = -1;

    var close = document.createElement('button');
    close.type = 'button';
    close.className = 'ma-applaunch-close';
    close.setAttribute('aria-label', 'Close ' + (cfg.title || 'application'));
    close.textContent = '✕';

    overlay.appendChild(buildFrame(cfg));
    overlay.appendChild(close);
    document.body.appendChild(overlay);
    document.documentElement.classList.add('ma-applaunch-open');

    var opener = document.activeElement;
    var done = false;

    function teardown() {
      if (done) return;
      done = true;
      document.removeEventListener('keydown', onKey, true);
      document.removeEventListener('fullscreenchange', onFsChange);
      document.removeEventListener('webkitfullscreenchange', onFsChange);
      exitFs();
      if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
      document.documentElement.classList.remove('ma-applaunch-open');
      if (opener && opener.focus) { try { opener.focus(); } catch (e) { /* gone */ } }
    }

    // Escape closes. Native fullscreen swallows Escape to exit fullscreen first, which fires
    // fullscreenchange and tears down there instead — so both paths land in the same place.
    function onKey(e) {
      if (e.key === 'Escape' || e.key === 'Esc') { e.stopPropagation(); teardown(); }
    }

    function onFsChange() {
      var fs = document.fullscreenElement || document.webkitFullscreenElement;
      if (!fs) teardown();
    }

    close.addEventListener('click', teardown);
    document.addEventListener('keydown', onKey, true);
    overlay.focus();

    requestFs(overlay).then(
      function () {
        document.addEventListener('fullscreenchange', onFsChange);
        document.addEventListener('webkitfullscreenchange', onFsChange);
      },
      function () {
        // No Fullscreen API, or the browser refused: the overlay is already position:fixed, so the
        // app is borderless within the tab. Say so rather than silently looking broken.
        overlay.classList.add('ma-applaunch-cover');
      });
  }

  /* ---- rung 3: a real separate window ---------------------------------------------------- */

  function openWindow(cfg) {
    var sep = cfg.url.indexOf('?') < 0 ? '?' : '&';
    var url = cfg.url + sep + FS_PARAM + '=1';
    var features = 'popup=yes,menubar=no,toolbar=no,location=no,status=no,resizable=yes'
                 + ',width=' + cfg.width + ',height=' + cfg.height;
    var w = null;
    try { w = window.open(url, cfg.target || '_blank', features); } catch (e) { w = null; }
    if (!w) { openOverlay(cfg); return; }        // popup blocked -> stay useful
    try { w.focus(); } catch (e) { /* focus is best-effort */ }
  }

  /* ---- the receiving half of mode="window" ----------------------------------------------- */

  // A page opened with ?ma-fs=1 cannot go fullscreen on load — it needs a gesture of its own.
  //
  // A bare document-level click listener is NOT enough here, and the failure is the common case
  // rather than the edge one: an app-host page is mostly a full-bleed <iframe>, so the visitor's
  // first click lands INSIDE it. A click in a nested browsing context never reaches this document,
  // the listener never fires, and the window stays chrome'd forever.
  //
  // So arm with a curtain that covers the viewport and swallows that first click — the same
  // "click to play" splash every browser game uses, for the same reason. It is removed the moment
  // it has served its purpose, so the app receives every click after it.
  function armFromQueryString() {
    if (window.location.search.indexOf(FS_PARAM + '=1') < 0) return;
    if (document.fullscreenElement) return;

    var curtain = document.createElement('div');
    curtain.className = 'ma-applaunch-arm';
    curtain.setAttribute('role', 'button');
    curtain.tabIndex = 0;
    curtain.setAttribute('aria-label', 'Click anywhere to go fullscreen');

    var pill = document.createElement('span');
    pill.className = 'ma-applaunch-arm-pill';
    pill.textContent = 'Click anywhere to go fullscreen';
    curtain.appendChild(pill);
    document.body.appendChild(curtain);

    var fired = false;
    function go() {
      if (fired) return;
      fired = true;
      cleanup();
      requestFs(document.documentElement).catch(function () { /* user can still press F11 */ });
    }
    function cleanup() {
      curtain.removeEventListener('click', go);
      document.removeEventListener('keydown', go);
      if (curtain.parentNode) curtain.parentNode.removeChild(curtain);
    }
    curtain.addEventListener('click', go);
    document.addEventListener('keydown', go);
    curtain.focus();
    // Never trap the page: drop the curtain even if the visitor ignores it.
    setTimeout(function () { if (!fired) cleanup(); }, 15000);
  }

  /* ---- wiring ---------------------------------------------------------------------------- */

  function configFrom(el) {
    return {
      url: attr(el, 'data-url', ''),
      title: attr(el, 'data-title', ''),
      mode: (attr(el, 'data-mode', 'fullscreen') || '').toLowerCase(),
      width: parseInt(attr(el, 'data-width', '1280'), 10) || 1280,
      height: parseInt(attr(el, 'data-height', '800'), 10) || 800,
      target: attr(el, 'data-target', '')
    };
  }

  function launch(el) {
    var cfg = configFrom(el);
    if (!cfg.url) return;
    if (cfg.mode === 'window') openWindow(cfg);
    else openOverlay(cfg);
  }

  document.addEventListener('click', function (e) {
    var trigger = e.target.closest ? e.target.closest('[data-ma-applaunch]') : null;
    if (!trigger) return;
    e.preventDefault();
    launch(trigger);
  });

  // Inline mode ships its own frame; its button just fullscreens the frame already on the page.
  document.addEventListener('click', function (e) {
    var btn = e.target.closest ? e.target.closest('[data-ma-applaunch-expand]') : null;
    if (!btn) return;
    e.preventDefault();
    var host = btn.closest('.ma-applaunch-inline');
    var frame = host && host.querySelector('.ma-applaunch-frame');
    if (frame) requestFs(frame).catch(function () { /* no API: leave it embedded */ });
  });

  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', armFromQueryString);
  else
    armFromQueryString();

  window.MindAtticAppLaunch = { launch: launch, openOverlay: openOverlay, openWindow: openWindow };
})();
