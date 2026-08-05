// Small JS interop helpers for Shanthi Nikethan Employee Management

window.siteHelpers = {
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

    /// Starts a 15-minute inactivity timer that signs the user out
    /// automatically. Runs as plain JS (not Blazor interop) so it keeps
    /// working even if the SignalR circuit drops. Any mouse, keyboard,
    /// scroll, or touch activity resets the timer.
    startIdleLogoutTimer: function () {
        var IDLE_LIMIT_MS = 15 * 60 * 1000;
        var timerId = null;

        function resetTimer() {
            if (timerId) clearTimeout(timerId);
            timerId = setTimeout(function () {
                window.location.href = '/MicrosoftIdentity/Account/SignOut';
            }, IDLE_LIMIT_MS);
        }

        ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'click'].forEach(function (evt) {
            document.addEventListener(evt, resetTimer, { passive: true });
        });

        resetTimer();
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

// Start the auto-logout idle timer immediately — runs on every page,
// independent of Blazor's render lifecycle.
window.siteHelpers.startIdleLogoutTimer();
