// Theme persistence for Shanthi Nikethan Employee Management
// Applies data-theme attribute on <html> and persists choice to localStorage.
window.theme = {
    STORAGE_KEY: 'snm-theme',

    get: function () {
        var saved = localStorage.getItem(this.STORAGE_KEY);
        if (saved) return saved;
        // Default to system preference if nothing saved yet
        var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        return prefersDark ? 'dark' : 'light';
    },

    set: function (value) {
        document.documentElement.setAttribute('data-theme', value);
        localStorage.setItem(this.STORAGE_KEY, value);
    },

    init: function () {
        this.set(this.get());
    }
};

// Apply immediately on script load (before Blazor renders) to avoid a flash
window.theme.init();
