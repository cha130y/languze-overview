import { expect, test } from '@playwright/test';

test('asks the tutor and sees the streamed reply', async ({ page }) => {
  await page.goto('/tutor');
  // Both browser projects share one learner, so earlier replies may already be here.
  const replies = page.getByText('The sofa is soft.', { exact: false });
  const before = await replies.count();

  // Typed straight after the page loads, as a learner would: on a slow phone that is
  // before React takes over, and the button must still wake for it.
  await page.getByLabel('ข้อความถึงติวเตอร์').fill('sofa แปลว่าอะไร');
  await page.getByRole('button', { name: 'ส่ง', exact: true }).click();

  // The fake provider's reply, which arrives in three pieces (NFR-003).
  await expect(replies).toHaveCount(before + 1);
});
