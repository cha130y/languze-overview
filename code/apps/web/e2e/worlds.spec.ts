import { expect, test } from '@playwright/test';
import { createWorld, FAKE_WORDS } from './support/app';

test('creates a world from a photo and lists the words found in it', async ({
  page,
}) => {
  await createWorld(page, 'ห้องนั่งเล่น');
  for (const word of FAKE_WORDS) {
    await expect(page.getByText(word, { exact: true })).toBeVisible();
  }
});

// Safari once pushed the photo field and its button past the edge of a phone screen.
test('the new-world page fits the width of the screen', async ({ page }) => {
  await page.goto('/worlds/new');
  await expect(page.getByLabel('รูปภาพ')).toBeVisible();
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - window.innerWidth,
  );
  expect(overflow).toBeLessThanOrEqual(0);
});
