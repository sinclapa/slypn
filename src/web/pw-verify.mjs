import { chromium } from '@playwright/test';
const browser = await chromium.launch();
const page = await browser.newPage();
await page.setViewportSize({ width: 1280, height: 900 });

await page.goto('http://localhost:5173/events');
await page.waitForLoadState('networkidle');
const calBtn = page.locator('button', { hasText: 'Calendar' });
if (await calBtn.count() > 0) { await calBtn.click(); await page.waitForTimeout(600); }
await page.screenshot({ path: 'C:/tmp/cal-verify.png' });

await page.goto('http://localhost:5173/admin');
await page.waitForLoadState('networkidle');
await page.waitForTimeout(600);
await page.screenshot({ path: 'C:/tmp/admin-verify.png', fullPage: true });

await browser.close();
console.log('done');
