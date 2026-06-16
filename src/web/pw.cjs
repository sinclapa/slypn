const { chromium } = require('@playwright/test');
(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.setViewportSize({ width: 1280, height: 900 });

  await page.goto('http://localhost:5173/events', { waitUntil: 'networkidle' });
  await page.click('button:has-text("Calendar")');
  await page.waitForTimeout(800);
  await page.screenshot({ path: 'C:/tmp/cal-verify.png' });

  await browser.close();
  console.log('done');
})().catch(e => { console.error(e.message); process.exit(1); });
