// JS interop used by AppHost.razor, PinPrompt.razor and the Lock page. Keep this file tiny: behavior lives in C#.
window.roastit = (function () {
    var root = document.documentElement;

    function applyTheme(dark, mode) {
        if (dark) root.setAttribute('data-theme', 'dark');
        else root.removeAttribute('data-theme');
        // Cache for the pre-paint script in index.html (not the source of truth).
        try { localStorage.setItem('roastline.themeCache', mode); } catch (e) { }
    }

    function setScrollLock(locked) {
        document.body.classList.toggle('no-scroll', !!locked);
    }

    // Escape closes the top layer (Windows). Android uses the hardware Back button instead.
    function registerEscape(dotnetRef) {
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                dotnetRef.invokeMethodAsync('OnEscape');
            }
        });
    }

    // Idle auto-lock: report activity at most every 5 s (plan §9.4: any pointer or key event).
    var activityRef = null;
    var lastReported = 0;
    function registerActivity(dotnetRef) { activityRef = dotnetRef; }
    ['pointerdown', 'touchstart', 'keydown', 'wheel', 'scroll'].forEach(function (name) {
        document.addEventListener(name, function () {
            if (!activityRef) return;
            var now = Date.now();
            if (now - lastReported < 5000) return;
            lastReported = now;
            activityRef.invokeMethodAsync('OnActivity').catch(function () { });
        }, { capture: true, passive: true });
    });

    // Physical keyboard drives the PIN keypad (lock screen and PIN prompt): 0-9, Backspace, Enter.
    // Ignored while typing in a text field.
    var keyRef = null;
    document.addEventListener('keydown', function (e) {
        if (!keyRef) return;
        var t = e.target;
        if (t && t.matches && t.matches('input, textarea, select')) return;
        var k = e.key;
        if (/^[0-9]$/.test(k) || k === 'Backspace' || k === 'Enter') {
            e.preventDefault();
            keyRef.invokeMethodAsync('OnKey', k).catch(function () { });
        }
    });
    function captureKeys(dotnetRef) { keyRef = dotnetRef; }
    function releaseKeys() { keyRef = null; }

    // Closes the on-screen keyboard when the PIN prompt opens.
    function blurActive() {
        var a = document.activeElement;
        if (a && a.blur && a !== document.body) a.blur();
    }

    // When the on-screen keyboard opens, keep the focused input of a sheet visible (plan §10.2).
    document.addEventListener('focusin', function (e) {
        var t = e.target;
        if (!t || !t.matches || !t.matches('input, textarea, select')) return;
        if (!t.closest('.sheet-body')) return;
        setTimeout(function () {
            try { t.scrollIntoView({ block: 'center', behavior: 'smooth' }); } catch (err) { }
        }, 300);
    });

    return {
        applyTheme: applyTheme,
        setScrollLock: setScrollLock,
        registerEscape: registerEscape,
        registerActivity: registerActivity,
        captureKeys: captureKeys,
        releaseKeys: releaseKeys,
        blurActive: blurActive
    };
})();
