// IdeaCmsBrochure: "Show demo login". Runs a Cloudflare Turnstile check, then asks the host for the demo's
// current login. The password is never in the page HTML; it is fetched on demand and written with
// textContent only. Event delegation, so it survives Blazor re-rendering the section.
(function () {
    'use strict';
    if (window.MaDemoAccess) return;
    window.MaDemoAccess = true;

    var TURNSTILE = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
    var loading = null;

    function loadTurnstile() {
        if (window.turnstile) return Promise.resolve(window.turnstile);
        if (loading) return loading;
        loading = new Promise(function (resolve, reject) {
            var s = document.createElement('script');
            s.src = TURNSTILE; s.async = true; s.defer = true;
            s.onload = function () { window.turnstile ? resolve(window.turnstile) : reject(new Error('Turnstile did not load')); };
            s.onerror = function () { loading = null; reject(new Error('Turnstile did not load')); };
            document.head.appendChild(s);
        });
        return loading;
    }

    function q(root, sel) { return root.querySelector(sel); }
    function say(root, text) { var m = q(root, '[data-ma-demo-msg]'); if (m) m.textContent = text || ''; }

    function untilText(iso) {
        if (!iso) return '';
        var mins = Math.round((new Date(iso).getTime() - Date.now()) / 60000);
        return mins > 0 ? 'This login works for about ' + mins + ' more minute' + (mins === 1 ? '' : 's') + '.' : 'This login is about to reset.';
    }

    function reveal(root, token) {
        say(root, 'Checking…');
        fetch(root.getAttribute('data-reveal-path') || '/_demo/reveal', {
            method: 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token: token })
        }).then(function (r) {
            return r.json().catch(function () { return {}; }).then(function (body) { return { ok: r.ok, status: r.status, body: body }; });
        }).then(function (res) {
            if (!res.ok) {
                say(root, res.status === 429 ? 'Too many tries. Wait a minute and try again.' : (res.body.error || 'Could not get the login.'));
                return;
            }
            q(root, '[data-ma-demo-user]').textContent = res.body.username || '';
            q(root, '[data-ma-demo-pass]').textContent = res.body.password || '';
            q(root, '[data-ma-demo-login]').hidden = false;
            q(root, '[data-ma-demo-check]').hidden = true;
            say(root, untilText(res.body.validUntilUtc));
        }).catch(function () { say(root, 'Could not reach the server.'); });
    }

    document.addEventListener('click', function (e) {
        var copy = e.target.closest('[data-ma-demo-copy]');
        if (copy) {
            var root = copy.closest('[data-ma-demo]');
            var field = q(root, copy.getAttribute('data-ma-demo-copy') === 'pass' ? '[data-ma-demo-pass]' : '[data-ma-demo-user]');
            if (field && navigator.clipboard) navigator.clipboard.writeText(field.textContent).then(function () { copy.textContent = 'Copied'; });
            return;
        }

        var btn = e.target.closest('[data-ma-demo-reveal]');
        if (!btn) return;
        var root = btn.closest('[data-ma-demo]');
        var check = q(root, '[data-ma-demo-check]');
        btn.disabled = true;
        check.hidden = false;
        say(root, '');
        loadTurnstile().then(function (ts) {
            ts.render(check, {
                sitekey: root.getAttribute('data-site-key'),
                action: 'demo-reveal',
                callback: function (token) { reveal(root, token); },
                'error-callback': function () { btn.disabled = false; say(root, 'The check failed to load. Try again.'); },
                'expired-callback': function () { btn.disabled = false; }
            });
        }).catch(function () { btn.disabled = false; say(root, 'The check failed to load. Try again.'); });
    });
})();
