// Pre-paint: sets data-theme-mode from the visitor's own saved choice (if any) before the theme CSS
// ever paints, so there's no flash of the wrong mode. The server-rendered attribute on <html> is only
// the first-visit default. Loaded as a same-origin file (not inline) because /login, /mfa and
// /account carry MindAttic.Authentication's strict CSP, which blocks inline scripts (M.IDEA-T36).
(function () {
  try {
    var m = localStorage.getItem('mindattic.theme-mode');
    if (m === 'light' || m === 'dark') document.documentElement.setAttribute('data-theme-mode', m);
  } catch (e) { }
})();
