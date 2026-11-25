import { expect, test } from 'vitest';
import { ARROW_LENGTH, arrowGeometry } from './question-arrow';

const big = { x: 0.1, y: 0.5, width: 0.8, height: 0.4 };
const down = { point: { x: 0.5, y: 0.7 }, tail: { x: 0.5, y: 0.2 } };

/* #73: the tip touches the object rather than covering it. */
test('the tip stops just short of the spot on a large object', () => {
  const geometry = arrowGeometry(big, down, 400, 400)!;

  expect(geometry.tip.x).toBeCloseTo(200);
  expect(geometry.tip.y).toBeCloseTo(274);
});

/* A desktop's photo once drew arrows twice as long as a phone's: now the length on screen is the same. */
test('an arrow is as long on a phone as on a desktop, in the direction the server chose', () => {
  const phone = arrowGeometry(big, down, 300, 300)!;
  const desktop = arrowGeometry(big, down, 480, 480)!;
  const length = (g: typeof phone) =>
    Math.hypot(g.tail.x - 300 * 0.5, g.tail.y - 300 * 0.7);

  expect(length(phone)).toBeCloseTo(ARROW_LENGTH.max);
  expect(
    Math.hypot(desktop.tail.x - 480 * 0.5, desktop.tail.y - 480 * 0.7),
  ).toBeCloseTo(ARROW_LENGTH.max);
  // Straight up from the spot, as the server's tail was.
  expect(phone.tail.x).toBeCloseTo(150);
});

test('a short arrow is drawn long enough to see', () => {
  const geometry = arrowGeometry(
    big,
    { point: { x: 0.5, y: 0.7 }, tail: { x: 0.5, y: 0.65 } },
    300,
    300,
  )!;

  expect(210 - geometry.tail.y).toBeCloseTo(ARROW_LENGTH.min);
});

/* A fixed 30px disc looked too big on a phone's photo, about 300px wide. */
test('the number disc is smaller on a phone than on a desktop', () => {
  expect(arrowGeometry(big, down, 300, 300)!.radius).toBe(10);
  expect(arrowGeometry(big, down, 480, 480)!.radius).toBe(15);
});

test('the number disc stays whole inside the photo', () => {
  const geometry = arrowGeometry(
    big,
    { point: { x: 0.5, y: 0.1 }, tail: { x: 0.5, y: 0.02 } },
    300,
    300,
  )!;

  expect(geometry.tail.y).toBeGreaterThanOrEqual(geometry.radius);
});

/* A toothbrush's box is a few pixels wide: the arrow stops at its edge, off the toothbrush. */
test('on a small object the tip stops at the edge of its box', () => {
  const geometry = arrowGeometry(
    { x: 0.45, y: 0.6, width: 0.05, height: 0.05 },
    { point: { x: 0.475, y: 0.625 }, tail: { x: 0.475, y: 0.2 } },
    400,
    400,
  )!;

  // The box's top edge is at 240 pixels.
  expect(geometry.tip.y).toBeCloseTo(238);
});

test('nothing is drawn before the photo has a size', () => {
  expect(arrowGeometry(big, down, 0, 0)).toBeNull();
});
