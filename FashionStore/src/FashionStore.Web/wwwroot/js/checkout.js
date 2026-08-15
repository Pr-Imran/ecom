// FashionStore - Multi-step checkout (Contact > Address > Delivery > Payment > Review)
(function () {
    'use strict';

    var STEPS = 5;
    var currentStep = 0;
    var lastResult = null;
    var selectedMethodId = null;

    var formEl = document.getElementById('checkout-form');
    var tokenInput = formEl ? formEl.querySelector('input[name="__RequestVerificationToken"]') : null;

    function getToken() {
        return tokenInput ? tokenInput.value : '';
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

    function money(n) {
        if (n === null || n === undefined || isNaN(n)) return '—';
        return '$' + Number(n).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function fieldValue(name) {
        var el = document.querySelector('[data-field="' + name + '"]');
        if (!el) return null;
        if (el.type === 'checkbox') return el.checked;
        return el.value === '' ? null : el.value;
    }

    function setError(field, message) {
        var targets = document.querySelectorAll('[data-error-for="' + field + '"]');
        targets.forEach(function (el) {
            el.textContent = message || '';
            var input = document.querySelector('[data-field="' + field + '"]');
            if (input) input.classList.toggle('form-error', !!message);
        });
    }

    function clearErrors() {
        document.querySelectorAll('.checkout-error').forEach(function (el) { el.textContent = ''; });
        document.querySelectorAll('.form-error').forEach(function (el) { el.classList.remove('form-error'); });
    }

    function buildAddress(prefix) {
        var keys = prefix === 'billing'
            ? { recipientName: 'billingRecipientName', addressLine1: 'billingAddressLine1', addressLine2: 'billingAddressLine2', area: 'billingArea', city: 'billingCity', region: 'billingRegion', postalCode: 'billingPostalCode', countryCode: 'billingCountryCode', deliveryInstructions: 'billingDeliveryInstructions', phone: 'billingPhone' }
            : { recipientName: 'recipientName', addressLine1: 'addressLine1', addressLine2: 'addressLine2', area: 'area', city: 'city', region: 'region', postalCode: 'postalCode', countryCode: 'countryCode', deliveryInstructions: 'deliveryInstructions', phone: 'phone' };

        var savedAddressId = fieldValue('savedAddressId');
        if (prefix !== 'billing' && savedAddressId) {
            return {
                savedAddressId: savedAddressId,
                recipientName: '',
                phone: null,
                addressLine1: '',
                addressLine2: null,
                area: null,
                city: '',
                region: null,
                postalCode: '',
                countryCode: 'US',
                deliveryInstructions: null
            };
        }

        return {
            savedAddressId: null,
            recipientName: fieldValue(keys.recipientName),
            phone: fieldValue(keys.phone),
            addressLine1: fieldValue(keys.addressLine1),
            addressLine2: fieldValue(keys.addressLine2),
            area: fieldValue(keys.area),
            city: fieldValue(keys.city),
            region: fieldValue(keys.region),
            postalCode: fieldValue(keys.postalCode),
            countryCode: fieldValue(keys.countryCode) || 'US',
            deliveryInstructions: fieldValue(keys.deliveryInstructions)
        };
    }

    function buildPayload() {
        var same = fieldValue('billingSameAsShipping') !== false;
        var shipping = buildAddress('shipping');
        return {
            guestEmail: fieldValue('guestEmail'),
            guestPhone: fieldValue('guestPhone'),
            shippingAddress: shipping,
            billingAddress: same ? null : buildAddress('billing'),
            billingSameAsShipping: same,
            shippingMethodId: selectedMethodId,
            paymentMethodCode: fieldValue('paymentMethodCode'),
            termsAccepted: fieldValue('termsAccepted') === true,
            continuationToken: lastResult ? lastResult.continuationToken : null
        };
    }

    function idempotencyKey() {
        var KEY = 'fs.checkout.idempotency';
        var existing = null;
        try { existing = sessionStorage.getItem(KEY); } catch (e) { existing = null; }
        if (existing) return existing;
        var key = 'ck-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 10);
        try { sessionStorage.setItem(KEY, key); } catch (e) { /* private mode */ }
        return key;
    }

    function showLoading(btn) {
        if (!btn) return;
        btn.disabled = true;
        btn.dataset.label = btn.textContent;
        btn.textContent = 'Please wait…';
    }

    function hideLoading(btn) {
        if (!btn) return;
        btn.disabled = false;
        if (btn.dataset.label) btn.textContent = btn.dataset.label;
    }

    function applyStepButtonState() {
        var continueBtn = document.querySelector('[data-checkout-continue]');
        var placeBtn = document.querySelector('[data-checkout-place-order]');
        var isFinal = currentStep === STEPS - 1;
        if (continueBtn) continueBtn.classList.toggle('hidden', isFinal);
        if (placeBtn) placeBtn.classList.toggle('hidden', !isFinal);
    }

    function goToStep(step) {
        currentStep = Math.max(0, Math.min(STEPS - 1, step));
        document.querySelectorAll('[data-step]').forEach(function (panel) {
            panel.classList.toggle('active', Number(panel.getAttribute('data-step')) === currentStep);
        });
        for (var i = 0; i < STEPS; i++) {
            var bar = document.querySelector('[data-step-bar="' + i + '"]');
            var label = document.querySelector('[data-step-label="' + i + '"]');
            if (!bar || !label) continue;
            var active = i === currentStep;
            var done = i < currentStep;
            bar.classList.toggle('active', active);
            bar.classList.toggle('done', done);
            label.classList.toggle('active', active);
            label.textContent = done ? '✓' + label.dataset.title : label.dataset.title;
        }
        var progress = document.querySelector('[data-checkout-progress]');
        if (progress) progress.setAttribute('aria-valuenow', String(currentStep + 1));
        applyStepButtonState();
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    function renderShippingOptions(result) {
        var list = document.querySelector('[data-delivery-list]');
        if (!list) return;
        var empty = list.querySelector('[data-delivery-empty]');
        if (empty) empty.remove();

        var options = result.shippingOptions || [];
        if (options.length === 0) {
            list.innerHTML = '<p class="text-sm text-brand-text-muted">No delivery options are available for this destination.</p>';
            return;
        }

        var html = options.map(function (q, index) {
            var selected = selectedMethodId === q.methodId;
            var disabled = !q.isAvailable;
            var price = q.isFree ? 'Free' : money(q.price);
            var window = q.estimatedMinDays && q.estimatedMaxDays
                ? ' (' + q.estimatedMinDays + '–' + q.estimatedMaxDays + ' days)'
                : '';
            var reason = disabled && q.unavailableReason ? '<span class="block text-xs text-brand-danger">' + q.unavailableReason + '</span>' : '';
            return '<label class="select-card block' + (selected ? ' selected' : '') + (disabled ? ' opacity-60' : '') + '" data-delivery-card data-method-id="' + q.methodId + '">' +
                '<input type="radio" name="shippingMethod" value="' + q.methodId + '" class="hidden"' + (disabled ? ' disabled' : '') + (selected ? ' checked' : '') + '>' +
                '<span class="flex items-center justify-between">' +
                '<span class="flex items-center gap-3">' +
                '<span class="w-5 h-5 rounded-full border border-brand-border flex items-center justify-center"><span class="w-2.5 h-2.5 rounded-full bg-brand-primary hidden" data-check></span></span>' +
                '<span>' +
                '<span class="block text-sm font-bold text-brand-text-primary">' + q.name + '</span>' +
                '<span class="block text-xs text-brand-text-muted">' + q.code + window + '</span>' +
                reason +
                '</span>' +
                '</span>' +
                '<span class="text-sm font-bold text-brand-text-primary">' + price + '</span>' +
                '</span>' +
                '</label>';
        }).join('');

        list.innerHTML = html;

        list.querySelectorAll('[data-delivery-card]').forEach(function (card) {
            card.addEventListener('click', function () {
                if (card.querySelector('input').disabled) return;
                selectedMethodId = card.getAttribute('data-method-id');
                list.querySelectorAll('[data-delivery-card]').forEach(function (c) {
                    var sel = c.getAttribute('data-method-id') === selectedMethodId;
                    c.classList.toggle('selected', sel);
                    c.querySelector('[data-check]').classList.toggle('hidden', !sel);
                });
                refreshCalculation();
            });
        });

        var firstAvailable = options.find(function (q) { return q.isAvailable; });
        if (!selectedMethodId && firstAvailable) {
            selectedMethodId = firstAvailable.methodId;
            renderShippingOptions(result);
        }
    }

    function renderPayment(result) {
        document.querySelectorAll('[data-payment-card]').forEach(function (card) {
            var code = card.getAttribute('data-code');
            var selected = (result.selectedShipping && code === 'cod' && !result.selectedShipping.supportsCashOnDelivery) ? false : (fieldValue('paymentMethodCode') === code);
            card.classList.toggle('selected', selected);
            card.querySelector('[data-check]').classList.toggle('hidden', !selected);
        });
    }

    function renderReview(result) {
        var lines = result.lines || [];
        lines.forEach(function (line) {
            var priceEl = document.querySelector('[data-review-line-price="' + line.variantId + '"]');
            if (priceEl) priceEl.textContent = money(line.lineTotal);
            var compareEl = document.querySelector('[data-review-line-compare="' + line.variantId + '"]');
            if (compareEl && line.compareAtPrice) {
                compareEl.textContent = money(line.compareAtPrice);
                compareEl.style.display = '';
            }
        });

        var totals = result.totals;
        if (totals) {
            setText('[data-review-subtotal]', money(totals.subtotal));
            setText('[data-review-shipping]', totals.isFreeShipping ? 'Free' : money(totals.shipping));
            setText('[data-review-total]', money(totals.grandTotal));
            setText('[data-review-currency]', 'All prices in ' + totals.currency);

            var promotions = totals.promotionsDiscount > 0;
            toggle('[data-review-promotions]', promotions);
            if (promotions) setText('[data-review-promotions-amount]', '-' + money(totals.promotionsDiscount));

            var coupon = totals.couponDiscount > 0;
            toggle('[data-review-coupon]', coupon);
            if (coupon) setText('[data-review-coupon-amount]', '-' + money(totals.couponDiscount));

            var tax = totals.tax > 0;
            toggle('[data-review-tax]', tax);
            if (tax) setText('[data-review-tax-amount]', money(totals.tax));

            var footerSubtotal = document.querySelector('[data-footer-subtotal]');
            if (footerSubtotal) footerSubtotal.textContent = money(totals.grandTotal);

            var summarySubtotal = document.querySelector('[data-summary-subtotal]');
            if (summarySubtotal) summarySubtotal.textContent = money(totals.subtotal);
            var summaryShipping = document.querySelector('[data-summary-shipping]');
            if (summaryShipping) summaryShipping.textContent = totals.isFreeShipping ? 'Free' : money(totals.shipping);
            var summaryTax = document.querySelector('[data-summary-tax]');
            if (summaryTax) summaryTax.textContent = money(totals.tax);
            toggle('[data-summary-shipping-row]', totals.shipping > 0 || totals.isFreeShipping);
            toggle('[data-summary-tax-row]', tax);
            var summaryTotal = document.querySelector('[data-summary-total]');
            if (summaryTotal) summaryTotal.textContent = money(totals.grandTotal);

            var footerShipping = document.querySelector('[data-footer-shipping]');
            if (footerShipping) footerShipping.textContent = totals.isFreeShipping ? 'Free' : money(totals.shipping);
            toggle('[data-footer-shipping-row]', totals.shipping > 0 || totals.isFreeShipping);
        }

        var shipping = result.selectedShipping;
        if (shipping) {
            var line = document.querySelector('[data-review-address-line]');
            if (line) {
                var addr = document.querySelector('[data-field="savedAddressId"]');
                var label = addr && addr.value ? 'Saved address selected' : 'Shipping address entered on step 2';
                line.textContent = label;
            }
        }
    }

    function setText(selector, value) {
        var el = document.querySelector(selector);
        if (el) el.textContent = value;
    }

    function toggle(selector, show) {
        var el = document.querySelector(selector);
        if (el) el.classList.toggle('hidden', !show);
    }

    function refreshCalculation() {
        var btn = currentStep === STEPS - 1
            ? document.querySelector('[data-checkout-place-order]')
            : document.querySelector('[data-checkout-continue]');
        showLoading(btn);

        return post('/checkout/calculate', buildPayload())
            .then(function (result) {
                lastResult = result;

                clearErrors();
                if (result.errors) {
                    result.errors.forEach(function (err) {
                        setError(err.field, err.message);
                    });
                }

                if (result.warnings && result.warnings.length) {
                    result.warnings.forEach(function (w) { window.showToast(w, 'warning'); });
                }

                if (result.shippingOptions && currentStep === 2) {
                    renderShippingOptions(result);
                }

                if (result.isValid) {
                    renderReview(result);
                    return true;
                }
                return false;
            })
            .catch(function () {
                window.showToast('Could not update your checkout.', 'danger');
                return false;
            })
            .finally(function () {
                hideLoading(btn);
            });
    }

    function nextStep() {
        var isFinal = currentStep === STEPS - 1;
        if (isFinal) {
            window.showToast('Place Order is ready for the next phase.', 'info');
            return;
        }

        refreshCalculation().then(function (ok) {
            if (!ok) {
                window.showToast('Please fix the highlighted fields.', 'warning');
                return;
            }
            var next = currentStep + 1;

            if (next === 1) {
                var saved = fieldValue('savedAddressId');
                if (saved) {
                    fillAddressFromSaved(saved);
                }
            }

            if (next === 2) {
                renderShippingOptions(lastResult);
            }

            goToStep(next);
        });
    }

    function fillAddressFromSaved(id) {
        var options = document.querySelector('[data-field="savedAddressId"]');
        if (!options) return;
        var selected = options.selectedOptions[0];
        if (!selected) return;
        var label = selected.textContent;
        // We only carry the id; the server resolves the full address at calculation time.
    }

    function backStep() {
        goToStep(currentStep - 1);
    }

    function placeOrder() {
        var btn = document.querySelector('[data-checkout-place-order]');
        showLoading(btn);

        refreshCalculation().then(function (ok) {
            if (!ok) {
                hideLoading(btn);
                window.showToast('Please fix the highlighted fields before placing your order.', 'warning');
                return;
            }

            var payload = buildPayload();
            payload.idempotencyKey = idempotencyKey();

            return post('/checkout/place', payload)
                .then(function (result) {
                    if (result && result.success && result.orderNumber) {
                        try { sessionStorage.removeItem('fs.checkout.idempotency'); } catch (e) { }
                        return initiatePayment(result.orderNumber, result.guestAccessToken);
                    }

                    clearErrors();
                    if (result && result.errors) {
                        result.errors.forEach(function (err) {
                            setError(err.field, err.message);
                            if (err.field === 'coupon' || err.field === 'items' || err.field === 'cart') {
                                window.showToast(err.message, 'warning');
                            }
                        });
                        if (result.errors.some(function (e) { return e.code === 'prices-changed'; })) {
                            window.showToast('Prices changed since you reviewed your order. Please review again.', 'warning');
                        }
                    } else {
                        window.showToast('We could not place your order. Please try again.', 'danger');
                    }
                    hideLoading(btn);
                })
                .catch(function () {
                    hideLoading(btn);
                    window.showToast('We could not reach the server. Your order has not been placed.', 'danger');
                });
        });
    }

    function confirmationUrl(orderNumber, guestToken) {
        var base = '/checkout/confirmation/' + encodeURIComponent(orderNumber);
        return guestToken ? base + '?t=' + encodeURIComponent(guestToken) : base;
    }

    function initiatePayment(orderNumber, guestToken) {
        var payload = guestToken ? { orderNumber: orderNumber, guestAccessToken: guestToken } : { orderNumber: orderNumber };
        return post('/checkout/pay', payload)
            .then(function (result) {
                if (result && result.success && result.payment) {
                    if (result.payment.redirectUrl) {
                        window.location.href = result.payment.redirectUrl;
                        return;
                    }
                }
                // Reference-based methods (COD, mobile wallet, bank transfer) and
                // hosted methods that return without a redirect land here.
                window.location.href = confirmationUrl(orderNumber, guestToken);
            })
            .catch(function () {
                // Initiation failed but the order is placed; the confirmation screen
                // offers a retry.
                window.location.href = confirmationUrl(orderNumber, guestToken);
            });
    }

    function init() {
        document.querySelectorAll('[data-step-label]').forEach(function (el) {
            el.dataset.title = el.textContent;
        });
        applyStepButtonState();

        var continueBtn = document.querySelector('[data-checkout-continue]');
        if (continueBtn) continueBtn.addEventListener('click', nextStep);

        var placeBtn = document.querySelector('[data-checkout-place-order]');
        if (placeBtn) placeBtn.addEventListener('click', function () {
            placeOrder();
        });

        var backBtn = document.querySelector('[data-checkout-back]');
        if (backBtn) {
            backBtn.addEventListener('click', function () {
                if (currentStep === 0) {
                    window.location.href = '/cart';
                } else {
                    backStep();
                }
            });
        }

        var summaryToggle = document.querySelector('[data-checkout-summary-toggle]');
        var sheet = document.querySelector('[data-summary-sheet]');
        if (summaryToggle && sheet) {
            summaryToggle.addEventListener('click', function () {
                sheet.classList.remove('hidden');
                sheet.setAttribute('aria-hidden', 'false');
            });
            sheet.querySelector('[data-summary-close]').addEventListener('click', closeSummary);
            sheet.querySelector('[data-summary-backdrop]').addEventListener('click', closeSummary);
        }

        var sameCheckbox = document.querySelector('[data-field="billingSameAsShipping"]');
        if (sameCheckbox) {
            sameCheckbox.addEventListener('change', function () {
                var billing = document.querySelector('[data-billing-form]');
                if (billing) billing.style.display = sameCheckbox.checked ? 'none' : 'block';
            });
        }

        var savedSelect = document.querySelector('[data-field="savedAddressId"]');
        if (savedSelect) {
            savedSelect.addEventListener('change', function () {
                refreshCalculation();
            });
        }
    }

    function closeSummary() {
        var sheet = document.querySelector('[data-summary-sheet]');
        if (sheet) {
            sheet.classList.add('hidden');
            sheet.setAttribute('aria-hidden', 'true');
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
