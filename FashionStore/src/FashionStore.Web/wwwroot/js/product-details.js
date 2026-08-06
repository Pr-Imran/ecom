// FashionStore - Product Details Page
(function () {
    'use strict';

    var dataEl = document.getElementById('product-details-data');
    if (!dataEl) return;

    var data;
    try {
        data = JSON.parse(dataEl.textContent);
    } catch (e) {
        return;
    }

    var currency = data.currency || '$';
    var selection = {}; // optionSlug -> valueSlug
    var currentCombo = null;
    var defaultVariant = null;

    // Map value id -> { optionSlug, valueSlug }
    var valueIdMap = {};
    data.options.forEach(function (option) {
        option.values.forEach(function (value) {
            valueIdMap[value.id] = { optionSlug: option.slug, valueSlug: value.slug };
        });
    });

    // Default selection from the default variant (or first variant)
    data.variants.forEach(function (v) {
        if (v.isDefault) defaultVariant = v;
    });
    if (!defaultVariant && data.variants.length) defaultVariant = data.variants[0];

    if (defaultVariant) {
        Object.keys(defaultVariant.attributeValueIds || {}).forEach(function (slug) {
            var entry = valueIdMap[defaultVariant.attributeValueIds[slug]];
            if (entry) selection[slug] = entry.valueSlug;
        });
    }

    function comboHasValue(combo, attrSlug, valueSlug) {
        var i = combo.attributeSlugs.indexOf(attrSlug);
        return i >= 0 && combo.valueSlugs[i] === valueSlug;
    }

    function comboMatchesSelection(combo, sel) {
        for (var slug in sel) {
            if (Object.prototype.hasOwnProperty.call(sel, slug)) {
                if (!comboHasValue(combo, slug, sel[slug])) return false;
            }
        }
        return true;
    }

    function isValueSelectable(optionSlug, valueSlug) {
        var other = {};
        for (var slug in selection) {
            if (slug !== optionSlug) other[slug] = selection[slug];
        }
        for (var i = 0; i < data.combinations.length; i++) {
            var combo = data.combinations[i];
            if (!combo.isAvailable) continue;
            if (!comboHasValue(combo, optionSlug, valueSlug)) continue;
            if (comboMatchesSelection(combo, other)) return true;
        }
        return false;
    }

    function allOptionsSelected() {
        return data.options.every(function (option) {
            return !!selection[option.slug];
        });
    }

    function findSelectedCombination() {
        for (var i = 0; i < data.combinations.length; i++) {
            var combo = data.combinations[i];
            if (!combo.isAvailable) continue;
            if (comboMatchesSelection(combo, selection)) return combo;
        }
        return null;
    }

    function variantById(id) {
        for (var i = 0; i < data.variants.length; i++) {
            if (data.variants[i].id === id) return data.variants[i];
        }
        return null;
    }

    function formatPrice(value) {
        var num = Number(value);
        return currency + (isNaN(num) ? '0.00' : num.toFixed(2));
    }

    function getFileName(url) {
        if (!url) return '';
        return String(url).split('?')[0].split('/').pop() || '';
    }

    // ---- UI updates ----

    function updatePriceDisplay() {
        var price, compare, sku, inStock, imageUrl;
        if (data.hasVariations) {
            currentCombo = allOptionsSelected() ? findSelectedCombination() : null;
        } else if (defaultVariant) {
            currentCombo = {
                variantId: defaultVariant.id,
                price: defaultVariant.price,
                compareAtPrice: defaultVariant.compareAtPrice,
                imageUrl: defaultVariant.imageUrl,
                isInStock: defaultVariant.isInStock
            };
        } else {
            currentCombo = null;
        }

        if (currentCombo) {
            var v = variantById(currentCombo.variantId);
            price = currentCombo.price != null ? currentCombo.price : (v ? v.price : data.basePrice);
            compare = (v && v.compareAtPrice != null && v.compareAtPrice > price) ? v.compareAtPrice : null;
            sku = v ? v.sku : data.baseSku;
            inStock = !!(currentCombo.isInStock && (v ? v.isInStock : true));
            imageUrl = currentCombo.imageUrl || (v ? v.imageUrl : null) || data.defaultImage;
        } else {
            price = defaultVariant ? defaultVariant.price : data.basePrice;
            compare = (defaultVariant && defaultVariant.compareAtPrice != null && defaultVariant.compareAtPrice > price)
                ? defaultVariant.compareAtPrice
                : data.baseCompareAtPrice;
            if (compare != null && compare <= price) compare = null;
            sku = defaultVariant ? defaultVariant.sku : data.baseSku;
            inStock = defaultVariant ? defaultVariant.isInStock : data.baseInStock;
            imageUrl = (defaultVariant && defaultVariant.imageUrl) || data.defaultImage;
        }

        var discount = (compare != null && compare > price)
            ? 'Save ' + Math.round((1 - price / compare) * 100) + '%'
            : null;

        document.querySelectorAll('[data-price]').forEach(function (el) {
            el.textContent = formatPrice(price);
        });
        document.querySelectorAll('[data-compare-price]').forEach(function (el) {
            el.textContent = compare != null ? formatPrice(compare) : '';
            el.classList.toggle('hidden', compare == null);
        });
        document.querySelectorAll('[data-discount]').forEach(function (el) {
            el.textContent = discount || '';
            el.classList.toggle('hidden', !discount);
        });
        document.querySelectorAll('[data-sku]').forEach(function (el) {
            el.textContent = sku;
        });
        document.querySelectorAll('[data-stock-text]').forEach(function (el) {
            el.innerHTML = inStock
                ? '<span class="text-brand-success font-medium">In stock</span>'
                : '<span class="text-brand-danger font-medium">Sold out</span>';
        });

        updateImages(imageUrl);
        updatePurchaseBar(inStock);
        updateAddButtons(inStock);
    }

    function updateImages(imageUrl) {
        var fileName = getFileName(imageUrl);
        var targetSlide = null;
        if (fileName) {
            document.querySelectorAll('[data-gallery-slide]').forEach(function (slide) {
                if (getFileName(slide.getAttribute('data-image-src')) === fileName) {
                    targetSlide = slide;
                }
            });
        }

        if (targetSlide) {
            var track = document.querySelector('[data-gallery-track]');
            if (track) targetSlide.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
            var idx = targetSlide.getAttribute('data-index');
            if (idx != null) setActiveDot(parseInt(idx, 10));
        }

        var mainImage = document.getElementById('product-main-image');
        if (mainImage && fileName && getFileName(mainImage.getAttribute('src')) !== fileName) {
            mainImage.setAttribute('src', imageUrl);
        }
        var sheetImage = document.querySelector('[data-sheet-image]');
        if (sheetImage && fileName && getFileName(sheetImage.getAttribute('src')) !== fileName) {
            sheetImage.setAttribute('src', imageUrl);
        }
    }

    function updatePurchaseBar(inStock) {
        var cta = document.querySelector('[data-purchase-bar-cta]');
        if (!cta) return;

        if (!data.hasVariations) {
            cta.textContent = 'Add to Cart';
            cta.disabled = !inStock;
            return;
        }

        if (currentCombo && inStock) {
            cta.textContent = 'Add to Cart';
            cta.disabled = false;
        } else {
            cta.textContent = 'Select Options';
            cta.disabled = false;
        }
    }

    function updateAddButtons(inStock) {
        var canAdd = !!currentCombo && inStock;
        document.querySelectorAll('[data-add-to-cart]').forEach(function (btn) {
            btn.disabled = !canAdd;
        });
    }

    function updateOptionStates() {
        document.querySelectorAll('[data-option-group]').forEach(function (group) {
            var optionSlug = group.getAttribute('data-option-slug');
            var selectedValue = selection[optionSlug];
            var selectedLabel = group.querySelector('[data-selected-label]');
            if (selectedLabel) {
                var selectedName = null;
                data.options.forEach(function (option) {
                    if (option.slug !== optionSlug) return;
                    option.values.forEach(function (value) {
                        if (value.slug === selectedValue) selectedName = value.name;
                    });
                });
                selectedLabel.textContent = selectedName ? selectedName : 'Select ' + (optionLabel(optionSlug) || 'option').toLowerCase();
            }

            group.querySelectorAll('[data-option-value]').forEach(function (btn) {
                var valueSlug = btn.getAttribute('data-value-slug');
                var baseAvailable = btn.getAttribute('data-available') === 'true';
                var selectable = baseAvailable && isValueSelectable(optionSlug, valueSlug);
                var isSelected = valueSlug === selectedValue;

                btn.disabled = !selectable;
                btn.setAttribute('aria-pressed', isSelected ? 'true' : 'false');
                btn.classList.toggle('option-swatch-selected', isSelected && btn.classList.contains('option-swatch'));
                btn.classList.toggle('option-chip-selected', isSelected && btn.classList.contains('option-chip'));
                btn.classList.toggle('option-swatch-disabled', !selectable && btn.classList.contains('option-swatch'));
                btn.classList.toggle('option-chip-disabled', !selectable && btn.classList.contains('option-chip'));
            });
        });
    }

    function optionLabel(slug) {
        for (var i = 0; i < data.options.length; i++) {
            if (data.options[i].slug === slug) return data.options[i].name;
        }
        return null;
    }

    function selectOption(optionSlug, valueSlug) {
        if (selection[optionSlug] === valueSlug) {
            delete selection[optionSlug];
        } else {
            selection[optionSlug] = valueSlug;
        }
        updateOptionStates();
        updatePriceDisplay();
    }

    // ---- Gallery ----

    function setActiveDot(index) {
        document.querySelectorAll('[data-gallery-dot]').forEach(function (dot) {
            var active = parseInt(dot.getAttribute('data-gallery-dot'), 10) === index;
            dot.classList.toggle('gallery-dot-active', active);
        });
    }

    function initGallery() {
        var track = document.querySelector('[data-gallery-track]');
        if (track) {
            track.addEventListener('scroll', function () {
                var slides = track.querySelectorAll('[data-gallery-slide]');
                if (!slides.length) return;
                var slideWidth = slides[0].getBoundingClientRect().width;
                if (!slideWidth) return;
                var index = Math.round(track.scrollLeft / slideWidth);
                index = Math.max(0, Math.min(slides.length - 1, index));
                setActiveDot(index);
                document.querySelectorAll('[data-thumb]').forEach(function (thumb) {
                    var active = parseInt(thumb.getAttribute('data-index'), 10) === index;
                    thumb.classList.toggle('gallery-thumb-active', active);
                });
            });
        }

        document.querySelectorAll('[data-thumb]').forEach(function (thumb) {
            thumb.addEventListener('click', function () {
                var index = parseInt(thumb.getAttribute('data-index'), 10);
                document.querySelectorAll('[data-thumb]').forEach(function (t) {
                    t.classList.toggle('gallery-thumb-active', t === thumb);
                });
                setActiveDot(index);
                var mainImage = document.getElementById('product-main-image');
                if (mainImage) mainImage.setAttribute('src', thumb.getAttribute('data-src'));
                var trackEl = document.querySelector('[data-gallery-track]');
                if (trackEl) {
                    var slides = trackEl.querySelectorAll('[data-gallery-slide]');
                    if (slides[index]) slides[index].scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
                }
            });
        });
    }

    // ---- Wishlist toggle ----

    function getWishlistState(btn) {
        return btn.getAttribute('aria-pressed') === 'true';
    }

    function setWishlistState(btn, active) {
        btn.setAttribute('aria-pressed', active ? 'true' : 'false');
        btn.querySelectorAll('[data-wishlist-icon]').forEach(function (icon) {
            icon.classList.toggle('fill-current', active);
            icon.classList.toggle('text-brand-danger', active);
        });
    }

    function initWishlist() {
        document.querySelectorAll('[data-wishlist]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var active = getWishlistState(btn);
                var variantId = null;
                if (currentCombo && currentCombo.variantId) {
                    variantId = currentCombo.variantId;
                }

                btn.disabled = true;
                fetch(active ? '/wishlist/remove' : '/wishlist/add', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': getToken()
                    },
                    body: JSON.stringify({
                        productId: data.productId,
                        variantId: variantId
                    })
                })
                    .then(function (res) {
                        if (!res.ok) throw new Error('bad-request');
                        return res.json();
                    })
                    .then(function (result) {
                        if (result && result.success) {
                            setWishlistState(btn, !active);
                            window.showToast(
                                active ? 'Removed from wishlist' : 'Added to wishlist',
                                active ? 'info' : 'success');
                        } else {
                            window.showToast(
                                (result && result.message) || 'Could not update wishlist',
                                'danger');
                        }
                    })
                    .catch(function () {
                        window.showToast('Could not update wishlist', 'danger');
                    })
                    .finally(function () {
                        btn.disabled = false;
                    });
            });
        });
    }

    // ---- Add to cart ----

    function getToken() {
        var input = document.querySelector('#antiforgery-form input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function addToCart(btn, qtyInputSelector) {
        if (btn && btn.disabled) return;
        if (!currentCombo) {
            window.showToast('Please select a valid combination', 'warning');
            return;
        }

        var qtyInput = document.querySelector(qtyInputSelector);
        var quantity = qtyInput ? parseInt(qtyInput.value, 10) || 1 : 1;
        var variantId = currentCombo.variantId;

        btn.disabled = true;
        var original = btn.textContent;
        btn.textContent = 'Adding...';

        fetch('/products/add-to-cart', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getToken()
            },
            body: JSON.stringify({
                productId: data.productId,
                variantId: variantId,
                quantity: quantity
            })
        })
            .then(function (res) {
                if (!res.ok) throw new Error('bad-request');
                return res.json();
            })
            .then(function (result) {
                if (result && result.success) {
                    window.showToast('Added to cart', 'success');
                    if (window.refreshCartCount) window.refreshCartCount();
                    if (window.closeBottomSheet) window.closeBottomSheet('variation-sheet');
                } else {
                    window.showToast((result && result.error) || 'Could not add to cart', 'danger');
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

    function initAddToCart() {
        document.querySelectorAll('[data-add-to-cart]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                addToCart(btn, btn.getAttribute('data-qty-input'));
            });
        });

        var barCta = document.querySelector('[data-purchase-bar-cta]');
        if (barCta) {
            barCta.addEventListener('click', function () {
                if (barCta.disabled) return;
                if (!data.hasVariations) {
                    if (defaultVariant) {
                        currentCombo = {
                            variantId: defaultVariant.id,
                            price: defaultVariant.price,
                            imageUrl: defaultVariant.imageUrl,
                            isInStock: defaultVariant.isInStock
                        };
                        addToCart(barCta, '#qty-sheet');
                    }
                } else if (currentCombo) {
                    addToCart(barCta, '#qty-sheet');
                } else {
                    window.openBottomSheet('variation-sheet');
                }
            });
        }
    }

    function init() {
        initGallery();
        initWishlist();
        initAddToCart();
        updateOptionStates();
        updatePriceDisplay();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
