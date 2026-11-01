/* IdeaCms.Component.CodeBlock — dresses every pre>code: adds .ma-code, a copy button, and a
 * data-lang badge. MutationObserver wires blocks rendered at any time. Safe to load more than once.
 * Instance settings (data-ma-settings="component.codeblock") tune labels and toggles; each falls back
 * to the as-designed behavior when absent. */
(function () {
  'use strict';
  if (window.__maCodeBlock) return;
  window.__maCodeBlock = true;

  function settings() {
    var el = document.querySelector('[data-ma-settings="component.codeblock"]');
    if (!el) return {};
    try { return JSON.parse(el.getAttribute('value') || '{}') || {}; } catch (e) { return {}; }
  }
  function text(v, fallback) { return typeof v === 'string' && v ? v : fallback; }

  function wire(pre) {
    if (pre.__maCodeWired || !pre.querySelector('code')) return;
    pre.__maCodeWired = true;
    pre.classList.add('ma-code');
    var cfg = settings();
    var copyText = text(cfg.copyText, 'Copy');
    var copiedText = text(cfg.copiedText, 'Copied');
    var copiedMs = parseInt(cfg.copiedDuration, 10);
    if (!(copiedMs >= 0)) copiedMs = 1600;

    var lang = pre.getAttribute('data-lang');
    if (lang && cfg.showLanguage !== false) {
      var badge = document.createElement('span');
      badge.className = 'ma-code-lang';
      badge.textContent = lang;
      pre.appendChild(badge);
    }

    if (cfg.showCopy === false) return;
    var copy = document.createElement('button');
    copy.type = 'button';
    copy.className = 'ma-code-copy';
    copy.textContent = copyText;
    copy.addEventListener('click', function () {
      var text = pre.querySelector('code').textContent;
      if (!navigator.clipboard) return; // clipboard API needs a secure context
      navigator.clipboard.writeText(text).then(function () {
        copy.textContent = copiedText;
        copy.classList.add('ma-code-copied');
        setTimeout(function () {
          copy.textContent = copyText;
          copy.classList.remove('ma-code-copied');
        }, copiedMs);
      }).catch(function () { /* permission denied — leave the button as "Copy" */ });
    });
    pre.appendChild(copy);
  }

  function wireAll(scope) {
    Array.prototype.forEach.call(scope.querySelectorAll('pre'), wire);
  }

  function start() {
    wireAll(document);
    new MutationObserver(function (muts) {
      muts.forEach(function (m) {
        Array.prototype.forEach.call(m.addedNodes, function (n) {
          if (n.nodeType !== 1) return;
          if (n.tagName === 'PRE') wire(n);
          else if (n.querySelectorAll) wireAll(n);
        });
      });
    }).observe(document.body, { childList: true, subtree: true });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
