(function () {
    'use strict';

    function ready(fn) {
        if (document.readyState !== 'loading') {
            fn();
        } else {
            document.addEventListener('DOMContentLoaded', fn);
        }
    }

    ready(function () {
        var avatarFile = document.getElementById('avatar-file');
        var avatarPreview = document.getElementById('avatar-preview');
        var avatarFallback = document.getElementById('avatar-fallback');

        if (avatarFile && avatarPreview) {
            avatarFile.addEventListener('change', function () {
                var file = avatarFile.files && avatarFile.files[0];
                if (!file) return;
                if (!file.type.startsWith('image/')) {
                    if (window.showToast) window.showToast('Please choose an image file.', 'warning');
                    return;
                }
                var reader = new FileReader();
                reader.onload = function (e) {
                    avatarPreview.src = e.target.result;
                    avatarPreview.classList.remove('hidden');
                    if (avatarFallback) avatarFallback.classList.add('hidden');
                    avatarFile.closest('form').submit();
                };
                reader.readAsDataURL(file);
            });
        }

        document.querySelectorAll('form[data-confirm]').forEach(function (form) {
            form.addEventListener('submit', function (e) {
                var message = form.getAttribute('data-confirm') || 'Are you sure?';
                if (!window.confirm(message)) {
                    e.preventDefault();
                }
            });
        });
    });
})();
