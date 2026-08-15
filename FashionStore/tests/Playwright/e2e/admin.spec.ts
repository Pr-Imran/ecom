import { test, expect } from '@playwright/test';

/**
 * Admin panel journeys.
 *
 * These specs require an authenticated admin session. The application seeds an
 * administrator (see RoleSeeder / AdminController.seed-superadmin in
 * Development); provide the credentials via FASHIONSTORE_ADMIN_EMAIL and
 * FASHIONSTORE_ADMIN_PASSWORD and the base URL via FASHIONSTORE_BASE_URL.
 */

const ADMIN_EMAIL = process.env.FASHIONSTORE_ADMIN_EMAIL ?? 'admin@fashionstore.local';
const ADMIN_PASSWORD = process.env.FASHIONSTORE_ADMIN_PASSWORD ?? 'Admin123!';

async function loginAsAdmin(page: import('@playwright/test').Page) {
  await page.goto('/account/login');
  await page.locator('#EmailOrUserName').fill(ADMIN_EMAIL);
  await page.locator('#Password').fill(ADMIN_PASSWORD);
  await page.getByRole('button', { name: 'Sign In' }).click();
  await page.waitForURL(/\/account/);
}

test.describe('admin authorization', () => {
  test('admin pages redirect anonymous visitors to login', async ({ page }) => {
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/account\/login/);
  });
});

test.describe('admin dashboard', () => {
  test('shows the dashboard after login', async ({ page }) => {
    await loginAsAdmin(page);
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/admin$/);
    await expect(page.locator('main')).toContainText('Dashboard');
  });
});

test.describe('admin product creation', () => {
  test('the product listing page renders and exposes the add-product action', async ({
    page,
  }) => {
    await loginAsAdmin(page);
    await page.goto('/admin/products');
    await expect(page.locator('main')).toContainText('Add Product');
    await expect(page.locator('#btnAdd')).toBeVisible();
  });
});

test.describe('admin invoice view', () => {
  test('the invoice endpoint responds to an authenticated admin', async ({
    page,
  }) => {
    await loginAsAdmin(page);

    const orders = await page.request.get('/api/admin/orders?page=1&pageSize=1');
    expect(orders.status()).toBe(200);

    const body = (await orders.json()) as { items?: { orderId: string }[] };
    const orderId = body.items?.[0]?.orderId;
    test.skip(!orderId, 'No orders exist to verify an invoice against');

    const invoice = await page.request.get(`/admin/orders/${orderId}/invoice`);
    expect(invoice.ok()).toBeTruthy();

    await page.goto(`/admin/orders/${orderId}/invoice`);
    await expect(page.locator('main')).toContainText('Invoice');
  });
});
