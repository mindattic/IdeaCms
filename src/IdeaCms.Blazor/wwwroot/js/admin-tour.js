// Lazy-loads driver.js from CDN and provides a Blazor-callable façade (MaTour) for first-visit
// onboarding tours across the admin section. Mirrors admin-monaco.js's lazy-CDN-load pattern.
(function () {
    'use strict';

    var CSS_URL = 'https://cdn.jsdelivr.net/npm/driver.js@1.3.1/dist/driver.css';
    var JS_URL = 'https://cdn.jsdelivr.net/npm/driver.js@1.3.1/dist/driver.js.iife.js';
    var SEEN_PREFIX = 'ma-admin-tour-seen:';

    var _ready = false;
    var _queue = [];
    var _cssReady = false;
    var _jsReady = false;

    function _maybeFlush() {
        // Wait for BOTH the stylesheet and the script: a <link> load doesn't block script
        // execution, so gating readiness on the script alone let driver() render its popover
        // before driver.css had arrived on a cold cache -- an unstyled, unreadable popup.
        if (!_cssReady || !_jsReady) return;
        _ready = true;
        var pending = _queue.splice(0);
        for (var i = 0; i < pending.length; i++) pending[i]();
    }

    function _run(fn) {
        if (_ready) { fn(); return; }
        _queue.push(fn);
        if (_queue.length > 1) return; // CDN load already in flight

        var link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = CSS_URL;
        link.onload = function () { _cssReady = true; _maybeFlush(); };
        link.onerror = function () { _cssReady = true; _maybeFlush(); }; // degrade to unstyled rather than hang forever
        document.head.appendChild(link);

        var script = document.createElement('script');
        script.src = JS_URL;
        script.onload = function () { _jsReady = true; _maybeFlush(); };
        document.head.appendChild(script);
    }

    window.MaTour = {
        // True once this browser has dismissed/completed the named tour (any way: done, close,
        // escape, clicking the overlay). Checked before auto-starting a tour on first visit to a tab.
        hasSeen: function (key) {
            try { return localStorage.getItem(SEEN_PREFIX + key) === '1'; }
            catch { return false; }
        },

        // Starts the named tour and marks it seen on teardown, however it ends, so it never
        // auto-starts again for this browser. `stepsJson` is an array of
        // { element, popover: { title, description } }; a step whose element isn't on the page
        // (e.g. an empty-state panel) is skipped rather than breaking the tour.
        start: function (key, stepsJson) {
            _run(function () {
                var steps;
                try { steps = JSON.parse(stepsJson); } catch { steps = []; }
                if (!steps.length) return;

                var driver = window.driver.js.driver;
                var tour = driver({
                    showProgress: steps.length > 1,
                    allowClose: true,
                    overlayClickBehavior: 'close',
                    skipMissingElement: true,
                    stagePadding: 6,
                    steps: steps,
                    onDestroyed: function () {
                        try { localStorage.setItem(SEEN_PREFIX + key, '1'); } catch { /* ignore */ }
                    }
                });
                tour.drive();
            });
        }
    };
}());
