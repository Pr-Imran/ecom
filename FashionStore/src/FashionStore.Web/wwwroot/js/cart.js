// FashionStore - Cart interactions (badge, drawer, quantity, remove, clear)
(function () {
    'use strict';

    var activeRequests = 0;

    function getToken() {
        var form = document.querySelector('#cart-antiforgery');
        var input = form ? form.querySelector('input[name="__RequestVerificationToken"]') : null;
        return input ? input.value : '';
    }

    function setCount(el, count) {
        if (!el) return;
        el.textContent = count > 0 ? String(count) : '';
        el.classList.toggle('hidden', count <= 0);
    }

    function updateAllBadges(count) {
        document.querySelectorAll('[data-cart-count]').forEach(function (el) {
            setCount(el, count);
        });
        document.querySelectorAll('[data-cart-drawer-count]').forEach(function (el) {
            el.textContent = count > 0 ? '(' + count + ')' : '';
        });
        document.querySelectorAll('[data-cart-page-count]').forEach(function (el) {
            el.textContent = '(' + count + ')';
        });
        document.querySelectorAll('[data-cart-page-count-desktop]').forEach(function (el) {
            el.textContent = count + (count === 1 ? ' item' : ' items');
        });
    }

    function updateTotalsFromMini(html) {
        var parser = new DOMParser();
        var doc = parser.parseFromString(html, 'text/html');
        var subtotalEl = doc.querySelector('[data-mini-subtotal]');
        var subtotal = subtotalEl ? subtotalEl.textContent : null;
        if (subtotal) {
            document.querySelectorAll('[data-cart-subtotal], [data-cart-total], [data-cart-mobile-subtotal]').forEach(function (el) {
                el.textContent = subtotal;
            });
        }
    }

    function post(url, payload) {
        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getToken()
            },
            body: JSON.stringify(payload || {})
        }).then(function (res) {
            if (!res.ok) throw new Error('bad-request');
            return res.json();
        });
    }

    function refreshCart(callback) {
        activeRequests++;
        fetch('/cart/mini', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (res) { return res.ok ? res.text() : ''; })
            .then(function (html) {
                var body = document.getElementById('cart-drawer-body');
                if (body && html) body.innerHTML = html;
                if (html) updateTotalsFromMini(html);
                bindMiniActions(body);
                if (callback) callback();
            })
            .catch(function () {
                if (callback) callback();
            })
            .finally(function () {
                activeRequests--;
            });
    }

    function bindMiniActions(container) {
        if (!container) return;
        container.querySelectorAll('[data-cart-checkout]').forEach(function (btn) {
            if (!btn.dataset.bound) {
                btn.dataset.bound = 'true';
                btn.addEventListener('click', checkout);
            }
        });
    }

    function afterMutation(payload) {
        var itemsRegion = document.querySelector('[data-cart-items]');
        return post(payload.url, payload.body).then(function (result) {
            if (!result || !result.success) {
                window.showToast((result && (result.message || result.error)) || 'Could not update cart', 'danger');
                return;
            }
            updateAllBadges(result.count);
            window.showToast(payload.successMessage, 'success');
            if (payload.after) payload.after(result);
        }).catch(function () {
            window.showToast('Could not update cart', 'danger');
        });
    }

    function changeQuantity(productId, variantId, delta) {
        var input = document.querySelector('[data-cart-qty][data-product-id="' + productId + '"][data-variant-id="' + variantId + '"]');
        if (!input) return;
        var min = parseInt(input.min || '1', 10);
        var max = parseInt(input.max || '99', 10);
        var current = parseInt(input.value || '1', 10);
        var next = current + delta;
        if (next < min || next > max) return;
        if (input.disabled) return;

        input.value = next;
        updateLineTotal(input, next);

        afterMutation({
            url: '/cart/update',
            body: { productId: productId, variantId: variantId, quantity: next },
            successMessage: 'Cart updated',
            after: function (result) {
                var line = input.closest('[data-cart-item]');
                if (result.count === 0) {
                    window.location.reload();
                    return;
                }
                refreshCart();
                if (line) {
                    var maxAttr = parseInt(input.max || '99', 10);
                    input.max = String(Math.max(next, maxAttr));
                }
            }
        });
    }

    function updateLineTotal(input, quantity) {
        var item = input.closest('[data-cart-item]');
        if (!item) return;
        var lineTotal = item.querySelector('[data-cart-line-total]');
        if (!lineTotal) return;
        var unit = item.querySelector('.price-current');
        if (!unit) return;
        var price = parseFloat(String(unit.textContent).replace(/[^0-9.-]/g, '')) || 0;
        lineTotal.textContent = '$' + (price * quantity).toFixed(2);
    }

    function removeItem(btn) {
        if (btn.disabled) return;
        btn.disabled = true;

        afterMutation({
            url: '/cart/remove',
            body: { productId: btn.getAttribute('data-product-id'), variantId: btn.getAttribute('data-variant-id') },
            successMessage: 'Removed from cart',
            after: function (result) {
                var item = btn.closest('[data-cart-item]');
                if (item) item.remove();
                if (result.count === 0) {
                    window.location.reload();
                    return;
                }
                refreshCart();
            }
        }).finally(function () {
            btn.disabled = false;
        });
    }

    function clearCart(btn) {
        if (btn.disabled) return;
        btn.disabled = true;

        afterMutation({
            url: '/cart/clear',
            body: {},
            successMessage: 'Cart cleared',
            after: function () {
                window.location.reload();
            }
        }).finally(function () {
            btn.disabled = false;
        });
    }

    function checkout() {
        var btn = arguments[0] && arguments[0].currentTarget ? arguments[0].currentTarget : null;
        if (btn && btn.disabled) return;
        window.location.href = '/checkout';
    }

    function fetchCount() {
        fetch('/cart/count')
            .then(function (res) { return res.ok ? res.json() : null; })
            .then(function (data) {
                if (data && typeof data.count === 'number') {
                    updateAllBadges(data.count);
                }
            })
            .catch(function () { /* badge stays hidden */ });
    }

    function init() {
        fetchCount();

        document.querySelectorAll('[data-cart-inc]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                changeQuantity(btn.getAttribute('data-product-id'), btn.getAttribute('data-variant-id'), 1);
            });
        });

        document.querySelectorAll('[data-cart-dec]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                changeQuantity(btn.getAttribute('data-product-id'), btn.getAttribute('data-variant-id'), -1);
            });
        });

        document.querySelectorAll('[data-cart-qty]').forEach(function (input) {
            input.addEventListener('change', function () {
                var productId = input.getAttribute('data-product-id');
                var variantId = input.getAttribute('data-variant-id');
                var min = parseInt(input.min || '1', 10);
                var max = parseInt(input.max || '99', 10);
                var value = parseInt(input.value || '1', 10);
                if (isNaN(value)) value = min;
                value = Math.max(min, Math.min(max, value));
                input.value = value;
                afterMutation({
                    url: '/cart/update',
                    body: { productId: productId, variantId: variantId, quantity: value },
                    successMessage: 'Cart updated',
                    after: function () { refreshCart(); }
                });
            });
        });

        document.querySelectorAll('[data-cart-remove]').forEach(function (btn) {
            btn.addEventListener('click', function () { removeItem(btn); });
        });

        document.querySelectorAll('[data-cart-clear]').forEach(function (btn) {
            btn.addEventListener('click', function () { clearCart(btn); });
        });

        document.querySelectorAll('[data-cart-checkout]').forEach(function (btn) {
            btn.addEventListener('click', checkout);
        });

        bindCouponActions();
        bindShippingActions();

        try {
            var savedShipping = JSON.parse(localStorage.getItem('fashionstore.shipping.selection') || 'null');
            if (savedShipping && savedShipping.country) {
                var savedCountry = document.querySelector('[data-cart-shipping-country]');
                var savedCity = document.querySelector('[data-cart-shipping-city]');
                if (savedCountry) savedCountry.value = savedShipping.country;
                if (savedCity && savedShipping.city) savedCity.value = savedShipping.city;
            }
        } catch (e) { /* storage unavailable */ }
    }

    function bindCouponActions() {
        document.querySelectorAll('[data-cart-coupon-toggle]').forEach(function (btn) {
            if (btn.dataset.bound) return;
            btn.dataset.bound = 'true';
            btn.addEventListener('click', function () {
                var form = document.querySelector('[data-cart-coupon-form]');
                if (form) form.classList.toggle('hidden');
            });
        });

        document.querySelectorAll('[data-cart-coupon-apply]').forEach(function (btn) {
            if (btn.dataset.bound) return;
            btn.dataset.bound = 'true';
            btn.addEventListener('click', function () { applyCoupon(btn); });
        });

        document.querySelectorAll('[data-cart-coupon-remove]').forEach(function (btn) {
            if (btn.dataset.bound) return;
            btn.dataset.bound = 'true';
            btn.addEventListener('click', function () { removeCoupon(btn); });
        });

        // Cart drawer: load mini cart each time it opens
        var drawerTriggers = document.querySelectorAll('[data-cart-drawer-open]');
        drawerTriggers.forEach(function (btn) {
            if (btn.dataset.bound) return;
            btn.dataset.bound = 'true';
            btn.addEventListener('click', function () {
                refreshCart(function () {
                    if (window.openDrawer) window.openDrawer('cart-drawer');
                });
            });
        });
    }

    function setCouponMessage(message, isError) {
        var input = document.querySelector('[data-cart-coupon-message]');
        if (input) {
            input.textContent = message || '';
            input.classList.toggle('text-brand-danger', !!isError);
            input.classList.toggle('text-brand-success', !isError);
        }
    }

    function applyCoupon(btn) {
        if (btn.disabled) return;
        var input = document.querySelector('[data-cart-coupon-input]');
        var code = input ? input.value.trim() : '';
        if (!code) {
            setCouponMessage('Enter a coupon code.', true);
            return;
        }

        btn.disabled = true;
        post('/cart/coupon', { code: code }).then(function (result) {
            if (!result || result.success === false) {
                var message = result && (result.message || result.error) ? (result.message || result.error) : 'This coupon cannot be applied.';
                setCouponMessage(message, true);
                window.showToast(message, 'danger');
                return;
            }
            setCouponMessage(result.message || 'Coupon applied', false);
            window.showToast(result.message || 'Coupon applied', 'success');
            refreshCartPageSummary();
        }).catch(function () {
            setCouponMessage('Could not apply coupon. Please try again.', true);
            window.showToast('Could not apply coupon', 'danger');
        }).finally(function () {
            btn.disabled = false;
        });
    }

    function removeCoupon(btn) {
        if (btn.disabled) return;
        btn.disabled = true;
        post('/cart/coupon/remove', {}).then(function (result) {
            window.showToast((result && result.message) || 'Coupon removed', 'success');
            refreshCartPageSummary();
        }).catch(function () {
            window.showToast('Could not remove coupon', 'danger');
        }).finally(function () {
            btn.disabled = false;
        });
    }

    function refreshCartPageSummary() {
        fetch('/cart/summary', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (res) { return res.ok ? res.text() : ''; })
            .then(function (html) {
                if (!html) return;
                var summaryRegion = document.querySelector('[data-cart-summary]');
                if (summaryRegion) summaryRegion.innerHTML = html;
                bindCouponActions();
                bindShippingActions();

                var parser = new DOMParser();
                var doc = parser.parseFromString(html, 'text/html');
                var subtotalEl = doc.querySelector('[data-cart-subtotal]');
                var totalEl = doc.querySelector('[data-cart-total]');
                if (subtotalEl) {
                    document.querySelectorAll('[data-cart-mobile-subtotal]').forEach(function (el) {
                        el.textContent = subtotalEl.textContent;
                    });
                }
                if (totalEl) {
                    document.querySelectorAll('[data-cart-total], [data-cart-page-total]').forEach(function (el) {
                        el.textContent = totalEl.textContent;
                    });
                }
                refreshCart();
            });
    }

    // ---- Shipping quote ----
    var shippingState = {
        country: '',
        city: '',
        selectedMethodId: null,
        quotes: [],
        quoted: false
    };

    function money(n) {
        var value = parseFloat(n);
        if (isNaN(value)) return '$0.00';
        return '$' + value.toFixed(2);
    }

    function etaText(q) {
        if (q.type === 3) return 'Local pickup';
        return q.estimatedMinDays + '–' + q.estimatedMaxDays + ' day delivery';
    }

    function bindShippingActions() {
        var quoteBtn = document.querySelector('[data-cart-shipping-quote-btn]');
        if (quoteBtn && !quoteBtn.dataset.bound) {
            quoteBtn.dataset.bound = 'true';
            quoteBtn.addEventListener('click', quoteShipping);
        }

        var country = document.querySelector('[data-cart-shipping-country]');
        var city = document.querySelector('[data-cart-shipping-city]');
        [country, city].forEach(function (input) {
            if (!input || input.dataset.bound) return;
            input.dataset.bound = 'true';
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    quoteShipping();
                }
            });
        });

        var options = document.querySelector('[data-cart-shipping-options]');
        if (options && !options.dataset.bound) {
            options.dataset.bound = 'true';
            options.addEventListener('change', function (e) {
                if (e.target && e.target.matches('input[data-shipping-method-radio]')) {
                    selectShippingMethod(e.target.value);
                }
            });
        }

        if (shippingState.quoted && document.querySelector('[data-cart-shipping-options]')) {
            renderShippingOptions();
            updateShippingCost();
        }
    }

    function setShippingMessage(message, isError) {
        var el = document.querySelector('[data-cart-shipping-message]');
        if (!el) return;
        el.textContent = message || '';
        el.classList.toggle('text-brand-danger', !!isError);
        el.classList.toggle('text-brand-text-muted', !isError);
    }

    function quoteShipping() {
        var countryInput = document.querySelector('[data-cart-shipping-country]');
        var cityInput = document.querySelector('[data-cart-shipping-city]');
        var countryCode = (countryInput ? countryInput.value : '').trim();
        var city = (cityInput ? cityInput.value : '').trim();

        if (!countryCode) {
            setShippingMessage('Enter a delivery country code (e.g. US).', true);
            return;
        }

        setShippingMessage('Calculating delivery options...', false);
        var btn = document.querySelector('[data-cart-shipping-quote-btn]');
        if (btn) btn.disabled = true;

        post('/cart/shipping/quote', { countryCode: countryCode, city: city || null, region: null, postalCode: null })
            .then(function (result) {
                shippingState.country = countryCode;
                shippingState.city = city;
                shippingState.quoted = true;
                shippingState.selectedMethodId = null;

                if (!result || result.isSupported === false) {
                    shippingState.quotes = [];
                    setShippingMessage((result && result.unsupportedReason) || 'We do not deliver to this destination yet.', true);
                    renderShippingOptions();
                    updateShippingCost();
                    return;
                }

                shippingState.quotes = (result.quotes || []).slice();
                renderShippingOptions();

                var available = shippingState.quotes.filter(function (q) { return q.isAvailable; });
                if (available.length === 0) {
                    setShippingMessage('No delivery methods are available for this destination.', true);
                } else {
                    setShippingMessage('', false);
                    selectShippingMethod(available[0].methodId);
                }
            })
            .catch(function () {
                setShippingMessage('Could not calculate delivery options. Please try again.', true);
            })
            .finally(function () {
                if (btn) btn.disabled = false;
            });
    }

    function renderShippingOptions() {
        var container = document.querySelector('[data-cart-shipping-options]');
        if (!container) return;
        container.innerHTML = '';

        if (shippingState.quotes.length === 0) {
            var empty = document.createElement('p');
            empty.className = 'text-xs text-brand-text-muted';
            empty.textContent = 'Enter a destination to see delivery options.';
            container.appendChild(empty);
            return;
        }

        var progressQuote = shippingState.quotes.find(function (q) {
            return q.isAvailable && q.remainingForFreeShipping !== null && q.remainingForFreeShipping > 0;
        });

        shippingState.quotes.forEach(function (q) {
            var label = document.createElement('label');
            label.className = 'flex items-start gap-3 rounded-brand-sm border p-3 cursor-pointer transition ' +
                (q.isAvailable ? 'hover:border-brand-primary' : 'opacity-60 cursor-not-allowed');
            if (q.isAvailable && q.methodId === shippingState.selectedMethodId) {
                label.classList.add('border-brand-primary', 'ring-1', 'ring-brand-primary');
            }

            var radio = document.createElement('input');
            radio.type = 'radio';
            radio.name = 'shipping-method';
            radio.value = q.methodId;
            radio.className = 'mt-1 w-4 h-4 text-brand-primary flex-shrink-0';
            radio.dataset.shippingMethodRadio = 'true';
            radio.checked = q.isAvailable && q.methodId === shippingState.selectedMethodId;
            radio.disabled = !q.isAvailable;

            var body = document.createElement('div');
            body.className = 'flex-1 min-w-0';
            var top = document.createElement('div');
            top.className = 'flex items-center justify-between gap-2';
            var name = document.createElement('span');
            name.className = 'text-sm font-semibold text-brand-text-primary';
            name.textContent = q.name;
            var price = document.createElement('span');
            price.className = 'text-sm font-bold ' + (q.isFree ? 'text-brand-success' : 'text-brand-text-primary');
            price.textContent = q.isFree ? 'Free' : money(q.price);
            top.appendChild(name);
            top.appendChild(price);
            var sub = document.createElement('p');
            sub.className = 'text-xs text-brand-text-muted mt-0.5';
            if (q.isAvailable) {
                sub.textContent = etaText(q) + (q.supportsCashOnDelivery ? ' · Cash on delivery available' : '');
            } else {
                sub.textContent = q.unavailableReason || 'Unavailable for this destination';
            }
            body.appendChild(top);
            body.appendChild(sub);

            label.appendChild(radio);
            label.appendChild(body);
            container.appendChild(label);
        });

        if (progressQuote) {
            var progressWrap = document.createElement('div');
            progressWrap.className = 'mt-3 rounded-brand-sm bg-brand-surface border border-brand-border p-3';
            var bar = document.createElement('div');
            bar.className = 'h-1.5 rounded-full bg-brand-border overflow-hidden';
            var fill = document.createElement('div');
            fill.className = 'h-full bg-brand-success rounded-full';
            var max = progressQuote.freeShippingThreshold || progressQuote.remainingForFreeShipping;
            var pct = max > 0 ? Math.max(0, Math.min(100, ((max - progressQuote.remainingForFreeShipping) / max) * 100)) : 0;
            fill.style.width = pct + '%';
            bar.appendChild(fill);
            var text = document.createElement('p');
            text.className = 'mt-2 text-xs text-brand-text-muted';
            text.textContent = 'Add ' + money(progressQuote.remainingForFreeShipping) + ' more for free ' + progressQuote.name.toLowerCase() + ' delivery.';
            progressWrap.appendChild(bar);
            progressWrap.appendChild(text);
            container.appendChild(progressWrap);
        }
    }

    function selectShippingMethod(methodId) {
        shippingState.selectedMethodId = methodId;
        var quote = shippingState.quotes.find(function (q) { return q.methodId === methodId; });
        if (quote && quote.isAvailable) {
            try {
                localStorage.setItem('fashionstore.shipping.selection', JSON.stringify({
                    country: shippingState.country,
                    city: shippingState.city,
                    methodId: methodId
                }));
            } catch (e) { /* storage unavailable */ }
        }
        renderShippingOptions();
        updateShippingCost();
    }

    function updateShippingCost() {
        var quote = shippingState.quotes.find(function (q) {
            return q.methodId === shippingState.selectedMethodId && q.isAvailable;
        });

        var costEl = document.querySelector('[data-cart-shipping-cost]');
        if (costEl) {
            costEl.textContent = quote ? (quote.isFree ? 'Free' : money(quote.price)) : 'Calculated at checkout';
        }

        var row = document.querySelector('[data-cart-mobile-shipping-row]');
        var costMobile = document.querySelector('[data-cart-mobile-shipping]');
        if (row && costMobile) {
            if (quote) {
                row.classList.remove('hidden');
                row.classList.add('flex');
                costMobile.textContent = quote.isFree ? 'Free' : money(quote.price);
            } else {
                row.classList.add('hidden');
                row.classList.remove('flex');
            }
        }
    }

    window.refreshCartCount = fetchCount;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
