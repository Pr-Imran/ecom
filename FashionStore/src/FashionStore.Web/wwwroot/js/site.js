// FashionStore - Component Interactions
(function () {
    'use strict';

    window.openModal = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        el.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
        trapFocus(el);
        document.addEventListener('keydown', function escapeHandler(e) {
            if (e.key === 'Escape') { closeModal(id); document.removeEventListener('keydown', escapeHandler); }
        });
    };

    window.closeModal = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        el.classList.add('hidden');
        document.body.style.overflow = '';
    };

    window.openBottomSheet = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        el.classList.remove('hidden');
        document.body.style.overflow = 'hidden';
        document.addEventListener('keydown', function escapeHandler(e) {
            if (e.key === 'Escape') { closeBottomSheet(id); document.removeEventListener('keydown', escapeHandler); }
        });
    };

    window.closeBottomSheet = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        el.classList.add('hidden');
        document.body.style.overflow = '';
    };

    window.openDrawer = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        el.classList.remove('hidden');
        var drawer = el.querySelector('.drawer-left, .drawer-right');
        if (drawer) {
            requestAnimationFrame(function () {
                drawer.classList.remove('-translate-x-full', 'translate-x-full');
            });
        }
        document.body.style.overflow = 'hidden';
        document.addEventListener('keydown', function escapeHandler(e) {
            if (e.key === 'Escape') { closeDrawer(id); document.removeEventListener('keydown', escapeHandler); }
        });
    };

    window.closeDrawer = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        var drawer = el.querySelector('.drawer-left, .drawer-right');
        if (drawer) {
            drawer.classList.add(drawer.classList.contains('drawer-right') ? 'translate-x-full' : '-translate-x-full');
            setTimeout(function () { el.classList.add('hidden'); document.body.style.overflow = ''; }, 200);
        } else {
            el.classList.add('hidden');
            document.body.style.overflow = '';
        }
    };

    window.toggleDropdown = function (id) {
        var el = document.getElementById(id);
        if (!el) return;
        var isHidden = el.classList.contains('hidden');
        document.querySelectorAll('.dropdown').forEach(function (d) { if (d.id !== id) d.classList.add('hidden'); });
        el.classList.toggle('hidden', !isHidden);
        var btn = el.previousElementSibling;
        if (btn) btn.setAttribute('aria-expanded', isHidden ? 'true' : 'false');
    };

    window.toggleAccordion = function (id) {
        var el = document.getElementById(id);
        var icon = document.getElementById(id + '-icon');
        if (!el) return;
        var isHidden = el.classList.contains('hidden');
        el.classList.toggle('hidden');
        var trigger = el.previousElementSibling;
        if (trigger) trigger.setAttribute('aria-expanded', isHidden ? 'true' : 'false');
        if (icon) icon.style.transform = isHidden ? 'rotate(180deg)' : 'rotate(0deg)';
    };

    window.switchTab = function (btn, index) {
        var tabs = btn.parentElement;
        if (!tabs) return;
        tabs.querySelectorAll('[role="tab"]').forEach(function (t, i) {
            var isActive = i === index;
            t.setAttribute('aria-selected', isActive ? 'true' : 'false');
            t.classList.toggle('tab-active', isActive);
            t.classList.toggle('tab', !isActive);
        });
    };

    window.updateQuantity = function (name, delta, min, max) {
        var input = document.getElementById(name);
        if (!input) return;
        var val = parseInt(input.value) || min;
        val += delta;
        val = Math.max(min, Math.min(max, val));
        input.value = val;
        var container = input.parentElement;
        if (container) {
            var btns = container.querySelectorAll('button');
            if (btns[0]) btns[0].disabled = val <= min;
            if (btns[1]) btns[1].disabled = val >= max;
        }
    };

    window.showToast = function (message, variant) {
        variant = variant || 'info';
        var container = document.getElementById('toast-container');
        if (!container) return;
        var icons = {
            success: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"/>',
            warning: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z"/>',
            danger: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z"/>',
            info: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"/>'
        };
        var colors = { success: 'text-green-500', warning: 'text-yellow-500', danger: 'text-red-500', info: 'text-blue-500' };
        var toast = document.createElement('div');
        toast.className = 'toast animate-slide-up';
        toast.setAttribute('role', 'alert');
        toast.innerHTML =
            '<svg class="w-5 h-5 flex-shrink-0 ' + (colors[variant] || colors.info) + '" fill="none" stroke="currentColor" viewBox="0 0 24 24">' +
            (icons[variant] || icons.info) +
            '</svg>' +
            '<p class="flex-1 text-sm">' + message + '</p>' +
            '<button class="btn-icon flex-shrink-0" onclick="this.parentElement.remove()" aria-label="Dismiss">' +
            '<svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/></svg>' +
            '</button>';
        container.appendChild(toast);
        setTimeout(function () { if (toast.parentElement) toast.remove(); }, 5000);
    };

    function trapFocus(element) {
        var focusable = element.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
        if (focusable.length === 0) return;
        var first = focusable[0];
        var last = focusable[focusable.length - 1];
        first.focus();
        element.addEventListener('keydown', function (e) {
            if (e.key === 'Tab') {
                if (e.shiftKey) {
                    if (document.activeElement === first) { e.preventDefault(); last.focus(); }
                } else {
                    if (document.activeElement === last) { e.preventDefault(); first.focus(); }
                }
            }
        });
    }

    document.addEventListener('click', function (e) {
        if (!e.target.closest('.dropdown') && !e.target.closest('[onclick*="toggleDropdown"]')) {
            document.querySelectorAll('.dropdown').forEach(function (d) { d.classList.add('hidden'); });
        }
    });
})();
