/* IdeaCms.Component.Carousel — wires any .ma-carousel of .ma-slide children: arrows, dots,
 * Left/Right keys, optional data-autoplay="ms" (pauses on hover/focus). A MutationObserver wires
 * carousels rendered at any time. Safe to load more than once. No external requests.
 * Page-wide defaults come from the instance settings (data-ma-settings="component.carousel");
 * a carousel's own data-autoplay / data-loop / data-arrows / data-dots / data-keyboard /
 * data-pause-on-hover / data-start attributes win over them. */
(function () {
  'use strict';
  if (window.__maCarousel) return;
  window.__maCarousel = true;

  function settings() {
    var el = document.querySelector('[data-ma-settings="component.carousel"]');
    if (!el) return {};
    try { return JSON.parse(el.getAttribute('value') || '{}') || {}; } catch (e) { return {}; }
  }

  // Element attribute > instance setting > as-designed default.
  function flag(root, attr, setting, fallback) {
    var v = root.getAttribute(attr);
    if (v !== null) return v !== 'false';
    return typeof setting === 'boolean' ? setting : fallback;
  }

  function wire(root) {
    if (root.__maCarouselWired) return;
    root.__maCarouselWired = true;
    var cfg = settings();

    var slides = Array.prototype.filter.call(root.children, function (el) {
      return el.classList.contains('ma-slide');
    });
    if (slides.length < 2) { if (slides[0]) slides[0].classList.add('ma-slide-active'); return; }

    var index = 0, timer = null;
    var attrInterval = root.getAttribute('data-autoplay');
    var interval = attrInterval !== null ? (parseInt(attrInterval, 10) || 0) : (parseInt(cfg.autoplay, 10) || 0);
    var loop = flag(root, 'data-loop', cfg.loop, true);
    var showArrows = flag(root, 'data-arrows', cfg.showArrows, true);
    var showDots = flag(root, 'data-dots', cfg.showDots, true);
    var keyboard = flag(root, 'data-keyboard', cfg.keyboard, true);
    var pauseOnHover = flag(root, 'data-pause-on-hover', cfg.pauseOnHover, true);
    var start = (parseInt(root.getAttribute('data-start') || cfg.startSlide, 10) || 1) - 1;
    if (start < 0 || start >= slides.length) start = 0;
    var prevText = typeof cfg.prevText === 'string' && cfg.prevText ? cfg.prevText : '‹';
    var nextText = typeof cfg.nextText === 'string' && cfg.nextText ? cfg.nextText : '›';
    var prevBtn = null, nextBtn = null;

    var dots = document.createElement('div');
    dots.className = 'ma-carousel-dots';
    var dotEls = slides.map(function (_, i) {
      var d = document.createElement('button');
      d.type = 'button';
      d.className = 'ma-carousel-dot';
      d.setAttribute('aria-label', 'Slide ' + (i + 1));
      d.addEventListener('click', function () { show(i); });
      dots.appendChild(d);
      return d;
    });

    function arrow(cls, text, dir) {
      var b = document.createElement('button');
      b.type = 'button';
      b.className = 'ma-carousel-arrow ' + cls;
      b.textContent = text;
      b.setAttribute('aria-label', dir > 0 ? 'Next slide' : 'Previous slide');
      b.addEventListener('click', function () { show(index + dir); });
      return b;
    }

    function show(i) {
      if (!loop) i = Math.max(0, Math.min(slides.length - 1, i));
      index = (i + slides.length) % slides.length;
      slides.forEach(function (s, n) { s.classList.toggle('ma-slide-active', n === index); });
      dotEls.forEach(function (d, n) { d.setAttribute('aria-current', n === index ? 'true' : 'false'); });
      if (!loop && prevBtn) { prevBtn.disabled = index === 0; nextBtn.disabled = index === slides.length - 1; }
    }

    if (showArrows) {
      prevBtn = arrow('ma-carousel-prev', prevText, -1);
      nextBtn = arrow('ma-carousel-next', nextText, 1);
      root.appendChild(prevBtn);
      root.appendChild(nextBtn);
    }
    if (showDots) root.appendChild(dots);
    root.tabIndex = 0;
    if (keyboard) {
      root.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowLeft') show(index - 1);
        else if (e.key === 'ArrowRight') show(index + 1);
      });
    }

    if (interval > 0) {
      // Autoplay always advances with wrap-around, even when manual navigation doesn't loop.
      var play = function () { stop(); timer = setInterval(function () { show(index + 1 >= slides.length ? 0 : index + 1); }, interval); };
      var stop = function () { if (timer) { clearInterval(timer); timer = null; } };
      if (pauseOnHover) {
        root.addEventListener('mouseenter', stop);
        root.addEventListener('mouseleave', play);
        root.addEventListener('focusin', stop);
        root.addEventListener('focusout', function (e) {
          if (root.contains(e.relatedTarget)) return; // focus moved within the carousel
          play();
        });
      }
      play();
    }
    show(start);
  }

  function wireAll(scope) {
    Array.prototype.forEach.call(scope.querySelectorAll('.ma-carousel'), wire);
  }

  function start() {
    wireAll(document);
    new MutationObserver(function (muts) {
      muts.forEach(function (m) {
        Array.prototype.forEach.call(m.addedNodes, function (n) {
          if (n.nodeType !== 1) return;
          if (n.classList && n.classList.contains('ma-carousel')) wire(n);
          else if (n.querySelectorAll) wireAll(n);
        });
      });
    }).observe(document.body, { childList: true, subtree: true });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
