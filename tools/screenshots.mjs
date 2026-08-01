/**
 * Reproducible README screenshots.
 *
 * The demo is deterministic (mock providers keyed by a stable hash), so running
 * this against a freshly started stack always produces byte-comparable images —
 * screenshots can never drift from what the code actually renders.
 *
 * Usage:
 *   1. dotnet run --project backend/src/JourneyOS.Showcase.Api      # API :5100
 *   2. cd frontend && npm run dev                                   # UI  :5173
 *   3. npx playwright@latest install chromium
 *      node tools/screenshots.mjs                                   # writes docs/images
 */
import { chromium } from "playwright";
import { mkdir } from "node:fs/promises";

const BASE = process.env.DEMO_URL ?? "http://localhost:5173";
const OUT = "docs/images";

await mkdir(OUT, { recursive: true });

const browser = await chromium.launch();
const page = await browser.newPage({
  viewport: { width: 1280, height: 900 },
  deviceScaleFactor: 2,
  colorScheme: "dark",
});

await page.goto(BASE, { waitUntil: "networkidle" });
await page.getByRole("button", { name: /Canonical demo/ }).click();
await page.getByText("Side-by-side comparison").waitFor({ timeout: 20_000 });
await page.waitForTimeout(400); // let the spinner/animation settle

const written = [];
const shot = async (name, label, fn) => {
  await fn();
  await page.screenshot({ path: `${OUT}/${name}.png` });
  written.push(`${OUT}/${name}.png — ${label}`);
};

// 1 — hero: form, the scored profile cards and the score breakdown
await shot("01-profiles", "search + scored profile cards + score breakdown", async () => {
  await page.evaluate(() => window.scrollTo(0, 0));
});

// 2 — the composed timeline with buffers, dual clocks and tz/date badges
await shot("02-timeline", "timeline with dual local clocks and tz/date badges", async () => {
  await page.locator("ol.timeline").scrollIntoViewIfNeeded();
  await page.evaluate(() => window.scrollBy(0, -200));   // keep the section heading in frame
});

// 3 — warning engine output (every row demo-flagged)
await shot("03-warnings", "rule-derived warnings, each flagged as demo data", async () => {
  await page.locator("ul.warnings").scrollIntoViewIfNeeded();
  await page.evaluate(() => window.scrollBy(0, -160));
});

// 4 — the side-by-side profile comparison (element shot: the whole card, no crop)
await shot("04-comparison", "side-by-side profile comparison", async () => {
  const card = page.locator("table.comparison").locator("xpath=ancestor::section[1]");
  await card.scrollIntoViewIfNeeded();
  await page.waitForTimeout(150);
});

await browser.close();
console.log(written.join("\n"));
