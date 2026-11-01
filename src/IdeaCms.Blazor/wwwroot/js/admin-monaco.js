// Lazy-loads Monaco editor from CDN and provides a Blazor-callable façade (MaMonaco).
// init() is called from MonacoEditor.razor via IJSRuntime after first render.
(function () {
    'use strict';

    var _ready = false;
    var _queue = [];
    var _tokens = [];
    var _completionsRegistered = false;
    var _editors = new Map();
    var _nextId = 0;

    function _run(fn) {
        if (_ready) { fn(); return; }
        _queue.push(fn);
        if (_queue.length > 1) return; // CDN load already in flight

        var cfg = document.createElement('script');
        cfg.textContent = "var require={paths:{vs:'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.52.2/min/vs'}};";
        document.head.appendChild(cfg);

        var loader = document.createElement('script');
        loader.src = 'https://cdnjs.cloudflare.com/ajax/libs/monaco-editor/0.52.2/min/vs/loader.js';
        loader.onload = function () {
            require(['vs/editor/editor.main'], function () {
                _ready = true;
                var pending = _queue.splice(0);
                for (var i = 0; i < pending.length; i++) pending[i]();
            });
        };
        document.head.appendChild(loader);
    }

    function _registerCompletions() {
        if (_completionsRegistered) return;
        _completionsRegistered = true;

        // Register catalog widget tags as known custom elements so Monaco's HTML validator
        // does not raise "Unknown HTML tag" warnings for <Component.X /> and <Plugin.X /> tags.
        if (monaco.languages.html && monaco.languages.html.htmlDefaults) {
            monaco.languages.html.htmlDefaults.setOptions({
                data: {
                    useDefaultDataProvider: true,
                    dataProviders: {
                        'ma-catalog': {
                            version: 1.1,
                            tags: _tokens.map(function (t) {
                                // Derive lowercase element name: "<Component.Tabboard />" → "component.tabboard"
                                return {
                                    name: t.label.replace(/^</, '').replace(/\s*\/>$/, '').toLowerCase(),
                                    description: t.kind + ': ' + t.displayName
                                };
                            })
                        }
                    }
                }
            });
        }

        monaco.languages.registerCompletionItemProvider('html', {
            triggerCharacters: ['<', '.'],
            provideCompletionItems: function (model, position) {
                var line = model.getLineContent(position.lineNumber);
                var textBefore = line.substring(0, position.column - 1);
                var ltPos = textBefore.lastIndexOf('<');
                if (ltPos === -1) return { suggestions: [] };
                var typed = textBefore.substring(ltPos); // includes '<'
                // Skip closing tags and HTML comments
                if (typed.startsWith('</') || textBefore.substring(ltPos - 3, ltPos) === '!--') {
                    return { suggestions: [] };
                }
                var replaceRange = {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: ltPos + 1,   // 1-based column of '<'
                    endColumn: position.column
                };
                return {
                    suggestions: _tokens
                        .filter(function (t) {
                            return t.token.toLowerCase().startsWith(typed.toLowerCase());
                        })
                        .map(function (t) {
                            return {
                                label: t.label,
                                kind: monaco.languages.CompletionItemKind.Snippet,
                                insertText: t.token,
                                detail: t.kind + ' — ' + t.displayName,
                                range: replaceRange
                            };
                        })
                };
            }
        });
    }

    window.MaMonaco = {
        init: function (el, dotnetRef, initialValue, tokenJson) {
            _tokens = tokenJson ? JSON.parse(tokenJson) : [];
            var id = 'mce' + (_nextId++);
            el.dataset.mceId = id;
            _run(function () {
                _registerCompletions();
                var editor = monaco.editor.create(el, {
                    value: initialValue || '',
                    language: 'html',
                    theme: 'vs-dark',
                    minimap: { enabled: false },
                    wordWrap: 'on',
                    lineNumbers: 'off',
                    scrollBeyondLastLine: false,
                    fontSize: 13,
                    automaticLayout: true
                });
                editor.onDidChangeModelContent(function () {
                    dotnetRef.invokeMethodAsync('OnValueChanged', editor.getValue());
                });
                _editors.set(id, editor);
            });
        },

        setValue: function (el, value) {
            var id = el && el.dataset && el.dataset.mceId;
            var ed = id && _editors.get(id);
            if (ed) {
                var v = value || '';
                if (ed.getValue() !== v) ed.setValue(v);
            }
        },

        dispose: function (el) {
            var id = el && el.dataset && el.dataset.mceId;
            var ed = id && _editors.get(id);
            if (ed) { ed.dispose(); _editors.delete(id); }
        }
    };
}());
