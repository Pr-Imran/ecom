(function () {
    'use strict';

    function getToken() {
        var form = document.querySelector('#wishlist-antiforgery');
        var input = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;
        return input ? input.value : '';
    }

    function updateCount(count) {
        document.querySelectorAll('[data-wishlist-count]').forEach(function (el) {
            el.textContent = '(' + count + ')';
        });
    }

    function removeFromWishlist(itemId, productId, variantId, btn) {
        var payload = { productId: productId, variantId: variantId || null };
        var url = '/wishlist/remove';

        if (btn && btn.hasAttribute('data-wishlist-item-id') && btn.getAttribute('data-wishlist-item-id')) {
            payload = { wishlistItemId: itemId };
            url = '/wishlist/remove-by-id';
        }

        var original = btn ? btn.innerHTML : '';
        if (btn) btn.disabled = true;

        fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getToken()
            },
            body: JSON.stringify(payload)
        })
            .then(function (res) {
                if (!res.ok) throw new Error('bad-request');
                return res.json();
            })
            .then(function (result) {
                if (result && result.success) {
                    var li = btn ? btn.closest('[data-wishlist-item]') : null;
                    if (li) li.remove();
                    updateCount(result.count);
                    if (!result.count && window.location.reload) {
                        // Reload to show the empty state when the list becomes empty.
                        window.location.reload();
                        return;
                    }
                    window.showToast('Removed from wishlist', 'info');
                } else {
                    window.showToast((result && result.message) || 'Could not remove item', 'danger');
                }
            })
            .catch(function () {
                window.showToast('Could not remove item', 'danger');
            })
            .finally(function () {
                if (btn) {
                    btn.disabled = false;
                    btn.innerHTML = original;
                }
            });
    }

    function moveToCart(btn) {
        var itemId = btn.getAttribute('data-wishlist-item-id');
        if (!itemId || btn.disabled) return;

        btn.disabled = true;
        var original = btn.textContent;
        btn.textContent = 'Moving...';

        fetch('/wishlist/move-to-cart', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getToken()
            },
            body: JSON.stringify({ wishlistItemId: itemId, quantity: 1 })
        })
            .then(function (res) {
                if (!res.ok) throw new Error('bad-request');
                return res.json();
            })
            .then(function (result) {
                if (result && result.success) {
                    var li = btn.closest('[data-wishlist-item]');
                    if (li) li.remove();
                    updateCount(result.count);
                    if (!result.count) {
                        window.location.reload();
                        return;
                    }
                    window.showToast('Added to cart', 'success');
                } else {
                    window.showToast((result && result.message) || 'Could not add to cart', 'danger');
                }
            })
            .catch(function () {
                window.showToast('Could not add to cart', 'danger');
            })
            .finally(function () {
                btn.disabled = false;
                btn.textContent = original;
            });
    }

    function init() {
        document.querySelectorAll('[data-wishlist-remove]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var itemId = btn.getAttribute('data-wishlist-item-id') || '';
                var productId = btn.getAttribute('data-wishlist-product-id') || '';
                var variantId = btn.getAttribute('data-wishlist-variant-id') || '';
                removeFromWishlist(itemId, productId, variantId, btn);
            });
        });

        document.querySelectorAll('[data-wishlist-move]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                moveToCart(btn);
            });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
