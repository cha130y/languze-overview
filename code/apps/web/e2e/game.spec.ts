import { expect, test, type Page } from '@playwright/test';
import { createWorld } from './support/app';

const QUESTION = 'สิ่งของในกรอบนี้ ภาษาอังกฤษเรียกว่าอะไร?';

/** Where the fake AI provider puts each word's box: left and top, in percent. */
const WORD_AT_BOX: Record<string, string> = {
  '5,45': 'sofa',
  '55,5': 'curtain',
  '20,70': 'coffee table',
  '12,78': 'rug',
  '72,55': 'plant',
  '78,8': 'clock',
};

const VARIANT: Record<string, string> = {
  sofa: 'couch',
  curtain: 'drape',
  'coffee table': 'table',
  rug: 'carpet',
  plant: 'houseplant',
};

/** The word the current question's box marks, read from where the box is drawn. */
async function askedWord(page: Page): Promise<string> {
  const style = await page
    .locator('span[aria-hidden="true"][style*="left"]')
    .getAttribute('style');
  const at = (name: string) =>
    Math.round(
      Number.parseFloat(
        style?.match(new RegExp(`${name}:\\s*([\\d.]+)%`))?.[1] ?? 'NaN',
      ),
    );
  const word = WORD_AT_BOX[`${at('left')},${at('top')}`];
  if (!word) throw new Error(`No fake word has a box at ${style}`);
  return word;
}

test('plays a whole game with right, accepted, wrong and unknown answers', async ({
  page,
}) => {
  await createWorld(page, 'เกมทดสอบ');
  await page.getByRole('button', { name: 'เริ่มเล่นเกม' }).click();
  await expect(page).toHaveURL(/\/sessions\/[0-9a-f-]{36}$/);

  const plan = ['right', 'accepted', 'wrong', 'unknown'] as const;
  const verdict = page
    .getByRole('status')
    .filter({ hasText: /ถูกต้อง|ไม่เป็นไร|ยังไม่ถูก/ });

  for (let question = 0; ; question++) {
    await expect(page.getByLabel(QUESTION)).toHaveValue('');
    const word = await askedWord(page);
    const step = plan[question] ?? 'right';

    if (step === 'unknown') {
      await page.getByRole('button', { name: 'ไม่ทราบ' }).click();
      await expect(verdict).toHaveText(/ไม่เป็นไร/);
    } else if (step === 'wrong') {
      await page.getByLabel(QUESTION).fill('banana');
      await page.getByRole('button', { name: 'ตอบ', exact: true }).click();
      await expect(verdict).toHaveText(/ยังไม่ถูก/);
    } else {
      const answer = step === 'accepted' ? (VARIANT[word] ?? word) : word;
      await page.getByLabel(QUESTION).fill(answer);
      await page.getByRole('button', { name: 'ตอบ', exact: true }).click();
      await expect(verdict).toHaveText(/ถูกต้อง \+10 XP/);
      if (answer !== word) {
        // The learner's own accepted answer is shown beside the main word.
        await expect(page.getByText('(ใช้ได้เช่นกัน)')).toBeVisible();
      }
    }

    const next = page.getByRole('button', { name: /ข้อต่อไป|ดูผลสรุป/ });
    const last = (await next.textContent())?.includes('ดูผลสรุป');
    await next.click();
    if (last) break;
  }

  await expect(page.getByRole('heading', { name: 'จบเกมแล้ว' })).toBeVisible();
});
