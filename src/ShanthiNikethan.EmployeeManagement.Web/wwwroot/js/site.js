// Small JS interop helpers for Shanthi Nikethan Employee Management

window.siteHelpers = {
    /// Copies text to the clipboard - returns true/false so the caller can show a confirmation.
    copyText: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).catch(function () { });
            return true;
        }
        return false;
    },

    /// Triggers a browser download for a byte array produced server-side.
    downloadFile: function (fileName, base64Content, mimeType) {
        var link = document.createElement('a');
        link.href = 'data:' + mimeType + ';base64,' + base64Content;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    },

    /// Focuses an element by id — useful after a modal/drawer opens.
    focusElement: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) el.focus();
    },

    /// Scrolls an element into view smoothly.
    scrollIntoView: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    },

    /// Returns true if the viewport is at the mobile breakpoint (matches
    /// the @media (max-width: 800px) rule in app.css).
    isMobileWidth: function () {
        return window.innerWidth <= 800;
    },

    _resizeHandler: null,
    _orientationHandler: null,

    /// Watches for the viewport crossing the mobile/desktop breakpoint
    /// (not every resize pixel - only actual crossings, debounced 150ms)
    /// and calls back into Blazor so layout state can be corrected live,
    /// without needing a page refresh. dotNetRef is a DotNetObjectReference
    /// to a component exposing a [JSInvokable] OnViewportBreakpointChanged(bool).
    ///
    /// Also listens for orientationchange separately from resize - iOS
    /// Safari has a well-known quirk where resize doesn't reliably fire on
    /// rotation, and window.innerWidth can briefly report the PRE-rotation
    /// value for a moment after orientationchange fires, so that path
    /// re-checks after a short delay rather than trusting the immediate value.
    registerBreakpointListener: function (dotNetRef) {
        window.siteHelpers.unregisterBreakpointListener();

        var lastIsMobile = window.siteHelpers.isMobileWidth();
        var debounceTimer = null;

        function checkAndNotify() {
            var isMobile = window.siteHelpers.isMobileWidth();
            if (isMobile !== lastIsMobile) {
                lastIsMobile = isMobile;
                dotNetRef.invokeMethodAsync('OnViewportBreakpointChanged', isMobile);
            }
        }

        function handleResize() {
            if (debounceTimer) clearTimeout(debounceTimer);
            debounceTimer = setTimeout(checkAndNotify, 150);
        }

        function handleOrientationChange() {
            // iOS specifically: window.innerWidth can lag behind the actual
            // new orientation for a beat, so re-check after a short delay
            // instead of trusting the value at the moment this event fires.
            setTimeout(checkAndNotify, 300);
        }

        window.addEventListener('resize', handleResize);
        window.addEventListener('orientationchange', handleOrientationChange);
        window.siteHelpers._resizeHandler = handleResize;
        window.siteHelpers._orientationHandler = handleOrientationChange;
    },

    /// Removes the listeners registered above — call this from the
    /// component's dispose logic so it doesn't keep firing (and keep a
    /// stale DotNetObjectReference alive) after the page/circuit is gone.
    unregisterBreakpointListener: function () {
        if (window.siteHelpers._resizeHandler) {
            window.removeEventListener('resize', window.siteHelpers._resizeHandler);
            window.siteHelpers._resizeHandler = null;
        }
        if (window.siteHelpers._orientationHandler) {
            window.removeEventListener('orientationchange', window.siteHelpers._orientationHandler);
            window.siteHelpers._orientationHandler = null;
        }
    },

    /// Starts an inactivity timer that signs the user out automatically -
    /// defaults to 15 minutes and the Entra sign-out path until
    /// configureIdleLogout() below corrects both, shortly after Blazor
    /// connects and MainLayout knows the real values (the configured
    /// server-side idle timeout, and whether this is a local-login
    /// session rather than Entra, which needs a different sign-out URL
    /// entirely). Runs as plain JS (not Blazor interop) so it keeps
    /// working even if the SignalR circuit drops. Any mouse, keyboard,
    /// scroll, or touch activity resets the timer.
    _idleLimitMs: 15 * 60 * 1000,
    _idleSignOutUrl: '/MicrosoftIdentity/Account/SignOut',
    _idleTimerId: null,

    configureIdleLogout: function (isLocalAuth, idleTimeoutMinutes) {
        window.siteHelpers._idleLimitMs = idleTimeoutMinutes * 60 * 1000;
        window.siteHelpers._idleSignOutUrl = isLocalAuth ? '/signin/logout' : '/MicrosoftIdentity/Account/SignOut';
        // Restart with the corrected values immediately, rather than
        // waiting for the next activity event to pick them up.
        window.siteHelpers._resetIdleTimer();
    },

    _resetIdleTimer: function () {
        if (window.siteHelpers._idleTimerId) clearTimeout(window.siteHelpers._idleTimerId);
        window.siteHelpers._idleTimerId = setTimeout(function () {
            window.location.href = window.siteHelpers._idleSignOutUrl;
        }, window.siteHelpers._idleLimitMs);
    },

    startIdleLogoutTimer: function () {
        ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'click'].forEach(function (evt) {
            document.addEventListener(evt, window.siteHelpers._resetIdleTimer, { passive: true });
        });

        window.siteHelpers._resetIdleTimer();
    },

    _charts: {},

    /// Renders (or re-renders) a Chart.js chart on the given canvas element.
    /// configJson is a JSON-encoded Chart.js config object — passed as a
    /// string rather than a plain object since that serializes reliably
    /// across the Blazor JS interop boundary. Destroys any previous chart
    /// on that canvas first, since components may re-render with new data.
    renderChart: function (canvasId, configJson) {
        var canvas = document.getElementById(canvasId);
        if (!canvas || typeof Chart === 'undefined') return;

        var config = JSON.parse(configJson);

        if (window.siteHelpers._charts[canvasId]) {
            window.siteHelpers._charts[canvasId].destroy();
        }
        window.siteHelpers._charts[canvasId] = new Chart(canvas, config);
    }
};

// ---- Anti-flash fix for the mobile sidebar, same pattern as theme.js ----
// Blazor's server-rendered HTML always starts the sidebar in its "open"
// (desktop-default) state, since the server can't know the viewport width.
// Waiting for the Blazor circuit to connect and correct this via interop
// (the original approach) took 2-3 real seconds on an actual device -
// visibly showing the wrong wide layout that whole time. This script runs
// synchronously, in document order, immediately after <Routes> has already
// rendered that markup but BEFORE the browser's first paint - so the
// correction happens instantly, with no visible flash, exactly like
// theme.js already does for avoiding a flash of the wrong color theme.
(function () {
    if (window.innerWidth <= 800) {
        var sidebar = document.querySelector('.sidebar');
        if (sidebar) sidebar.classList.remove('open');
    }
})();

// Start the auto-logout idle timer immediately — runs on every page,
// independent of Blazor's render lifecycle.
window.siteHelpers.startIdleLogoutTimer();
