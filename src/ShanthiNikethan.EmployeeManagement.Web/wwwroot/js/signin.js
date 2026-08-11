// Sign-in page behavior - previously inline onclick/onload attributes in
// LocalAccountController.cs's hand-written HTML, moved here specifically
// so the site's Content-Security-Policy can require script-src 'self'
// without needing 'unsafe-inline', which would otherwise allow any
// inline script to run (a real weakening of the protection CSP is meant
// to provide).

document.addEventListener('DOMContentLoaded', () => {
    const bgPhoto = document.querySelector('.signin-bg-photo');
    if (bgPhoto) {
        bgPhoto.addEventListener('load', () => bgPhoto.classList.add('loaded'));
        // Handles the case the image was already cached and loaded
        // before this listener attached.
        if (bgPhoto.complete) bgPhoto.classList.add('loaded');
    }

    document.querySelectorAll('img').forEach(img => {
        img.addEventListener('contextmenu', e => e.preventDefault());
    });

    const showFallbackBtn = document.getElementById('showFallbackForm');
    const backBtn = document.getElementById('backToChoice');
    const choice = document.getElementById('fallbackChoice');
    const form = document.getElementById('fallbackForm');
    const usernameField = document.getElementById('fallbackUsername');

    if (showFallbackBtn && choice && form) {
        showFallbackBtn.addEventListener('click', () => {
            choice.style.display = 'none';
            form.style.display = 'block';
            if (usernameField) usernameField.focus();
        });
    }

    if (backBtn && choice && form) {
        backBtn.addEventListener('click', () => {
            form.style.display = 'none';
            choice.style.display = 'block';
        });
    }
});
