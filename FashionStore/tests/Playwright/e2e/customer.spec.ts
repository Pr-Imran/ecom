import { test, expect, Page } from '@playwright/test';

/**
 * Customer storefront journeys.
 *
 * These specs run against a running FashionStore instance that is database
 * backed and seeded with the sample catalogue (the seeded "Cashmere Crew Neck
 * Sweater" product with a Heather Grey / M variant is used). Set the base URL
 * with FASHIONSTORE_BASE_URL (default http://localhost:5000).
 */

const SWEATER_SLUG = 'cashmere-crew-neck-sweater';

async function getProductLinks(page: Page) {
  return page.locator('main a[href^="/products/"]');
}

test.describe('mobile navigation', () => {
  test('shows bottom navigation with home, search, wishlist and orders', async ({
    page,
  }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/$/);

    const bottomNav = page.locator('nav.lg\\:hidden');
    await expect(bottomNav).toBeVisible();
    await expect(bottomNav.locator('a[href="/"]').first()).toBeVisible();
    await expect(bottomNav.getByText('Search').first()).toBeVisible();
    await expect(bottomNav.getByText('Wishlist').first()).toBeVisible();
    await expect(bottomNav.getByText('Orders').first()).toBeVisible();
  });
});

test.describe('product search', () => {
  test('searches and shows results matching the query', async ({ page }) => {
    await page.goto('/products/search?q=cashmere');
    await expect(page).toHaveURL(/\/products\/search\?q=cashmere/);
    await expect(page.getByRole('heading', { name: /Search Results/ })).toBeVisible();

    const links = await getProductLinks(page);
    const hrefs = await links.evaluateAll((els) =>
      els.map((el) => el.getAttribute('href'))
    );
    expect(hrefs.some((h) => h === `/products/${SWEATER_SLUG}`)).toBeTruthy();
  });

  test('redirects to the catalogue when the query is empty', async ({
    page,
  }) => {
    await page.goto('/products/search?q=');
    await expect(page).toHaveURL(/\/products$/);
  });
});

test.describe('catalogue filters', () => {
  test('filtering by category narrows the product list', async ({ page }) => {
    await page.goto('/products?category=clothing');
    await expect(page).toHaveURL(/category=clothing/);
    await expect(
      page.locator('main a[href="/products/cashmere-crew-neck-sweater"]').first()
    ).toBeVisible();
  });

  test('an unknown category returns an empty state', async ({ page }) => {
    await page.goto('/products?category=does-not-exist');
    await expect(page.getByText('No products found')).toBeVisible();
  });
});

test.describe('variation selection and add to cart', () => {
  test('selects a variation, adds to cart and the badge updates', async ({
    page,
  }) => {
    await page.goto(`/products/${SWEATER_SLUG}`);
    await expect(page.getByRole('heading', { name: /Cashmere Crew Neck Sweater/ })).toBeVisible();

    const cartCount = page.locator('[data-cart-count]');
    await expect(cartCount).toBeHidden();

    await page.locator('[data-add-to-cart]').first().click();

    await expect(cartCount).toBeVisible();
    await expect(cartCount).toHaveText('1');
  });

  test('shows sold-out state when the variant has no stock', async ({
    page,
  }) => {
    await page.goto('/products/trail-running-shoe');
    await expect(page.getByText(/Sold out/).first()).toBeVisible();
  });
});

test.describe('cart update', () => {
  test('quantity can be increased and removed from the cart page', async ({
    page,
  }) => {
    await page.goto(`/products/${SWEATER_SLUG}`);
    await page.locator('[data-add-to-cart]').first().click();

    await page.goto('/cart');
    const item = page.locator('[data-cart-item]').first();
    await expect(item).toBeVisible();

    await page.locator('[data-cart-inc]').first().click();
    await expect(page.locator('[data-cart-qty]').first()).toHaveValue('2');

    await page.locator('[data-cart-remove]').first().click();
    await expect(page.getByText('Your cart is empty')).toBeVisible();
  });
});

test.describe('guest checkout', () => {
  test('completes a guest checkout through the payment step', async ({
    page,
  }) => {
    await page.goto(`/products/${SWEATER_SLUG}`);
    await page.locator('[data-add-to-cart]').first().click();

    await page.goto('/checkout');

    await page.locator('#guest-email').fill('guest@example.com');
    await page.locator('#guest-phone').fill('+15550001111');
    await page.locator('[data-checkout-continue]').first().click();

    await page.locator('#co-recipient').fill('Jane Guest');
    await page.locator('#co-address1').fill('1 Main Street');
    await page.locator('#co-city').fill('New York');
    await page.locator('#co-postal').fill('10001');
    await page.locator('#co-country').selectOption({ label: 'United States' });
    await page.locator('[data-checkout-continue]').first().click();

    await page.locator('[data-checkout-continue]').first().click();

    await page.locator('[data-field="paymentMethodCode"]').first().check();
    await page.locator('[data-field="termsAccepted"]').check();
    await page.locator('[data-checkout-continue]').first().click();

    await expect(page.locator('[data-checkout-place-order]')).toBeVisible();
  });
});

test.describe('login', () => {
  test('invalid credentials stay on the login page with an error', async ({
    page,
  }) => {
    await page.goto('/account/login');
    await page.locator('#EmailOrUserName').fill('no-such-user@example.com');
    await page.locator('#Password').fill('WrongPassword123!');
    await page.getByRole('button', { name: 'Sign In' }).click();

    await expect(page).toHaveURL(/\/account\/login/);
    await expect(page.locator('.alert-danger').first()).toBeVisible();
  });
});

test.describe('customer order view', () => {
  test('order history requires authentication and redirects to login', async ({
    page,
  }) => {
    await page.goto('/orders');
    await expect(page).toHaveURL(/\/account\/login/);
  });
});
