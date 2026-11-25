'use client';

import { useLayoutEffect, useRef, useState } from 'react';
import type { Question } from '@/lib/api/client';

type Box = Question['box'];
type Arrow = NonNullable<Question['arrow']>;
type Spot = { x: number; y: number };

/**
 * The number disc's radius: a share of the photo's width, between these two. On a
 * phone's photo, about 300px wide, a fixed 30px disc looked too big beside an arrow
 * that short; on a desktop's, about 480px, it stays at 30px.
 */
export const BADGE_RADIUS = { min: 10, max: 15, share: 0.032 };
/**
 * The arrow's length on screen, whatever the photo's size: the server chose a length
 * as a share of the photo, which made desktop arrows twice as long as a phone's.
 * Its direction, chosen to keep off busy parts of the photo, is kept.
 */
export const ARROW_LENGTH = { min: 56, max: 100 };
/** How far short of the spot the tip stops, so it touches the object rather than covering it. */
const TIP_GAP = 6;
/** A box narrower than this is a small object: the tip stops at its edge, off the object itself. */
const SMALL_BOX = 48;

const clamp = (value: number, low: number, high: number) =>
  Math.min(Math.max(value, low), high);

/**
 * The arrow in pixels on a photo drawn this size (#73): from the tail, where the
 * number sits, to just short of the spot on the object. On a small object the tip
 * stops where the line meets its box instead, so the arrow never hides what it
 * points at. Returns null when the photo has no size yet.
 */
export function arrowGeometry(
  box: Box,
  arrow: Arrow,
  width: number,
  height: number,
): {
  tail: Spot;
  tip: Spot;
  headBase: Spot;
  angle: number;
  radius: number;
  scale: number;
} | null {
  if (width <= 0 || height <= 0) return null;
  const radius = clamp(
    width * BADGE_RADIUS.share,
    BADGE_RADIUS.min,
    BADGE_RADIUS.max,
  );
  // Shaft, head and edge shrink with the disc, so a phone's arrow keeps its proportions.
  const scale = radius / BADGE_RADIUS.max;
  const headLength = 14 * scale;

  const point = { x: arrow.point.x * width, y: arrow.point.y * height };
  const given = { x: arrow.tail.x * width, y: arrow.tail.y * height };
  const givenLength = Math.hypot(point.x - given.x, point.y - given.y);
  if (givenLength === 0) return null;
  const back = {
    x: (given.x - point.x) / givenLength,
    y: (given.y - point.y) / givenLength,
  };
  const length = clamp(givenLength, ARROW_LENGTH.min, ARROW_LENGTH.max);
  // The disc stays whole inside the photo.
  const tail = {
    x: clamp(point.x + back.x * length, radius + 2, width - radius - 2),
    y: clamp(point.y + back.y * length, radius + 2, height - radius - 2),
  };

  const span = Math.hypot(point.x - tail.x, point.y - tail.y);
  if (span === 0) return null;
  const direction = {
    x: (point.x - tail.x) / span,
    y: (point.y - tail.y) / span,
  };
  const rect = {
    left: box.x * width,
    top: box.y * height,
    right: (box.x + box.width) * width,
    bottom: (box.y + box.height) * height,
  };

  let reach = span - TIP_GAP;
  const small =
    Math.min(rect.right - rect.left, rect.bottom - rect.top) < SMALL_BOX;
  const entry = entryDistance(tail, direction, span, rect);
  if (small && entry !== null) reach = Math.min(reach, entry - 2);
  // Never so short that the head would sit under the number.
  reach = Math.max(reach, radius + headLength + 4);

  const tip = {
    x: tail.x + direction.x * reach,
    y: tail.y + direction.y * reach,
  };
  const headBase = {
    x: tip.x - direction.x * headLength,
    y: tip.y - direction.y * headLength,
  };
  return {
    tail,
    tip,
    headBase,
    angle: Math.atan2(direction.y, direction.x),
    radius,
    scale,
  };
}

/** How far along the line from the tail it first enters the box; null when the tail is already in it or the line misses. */
function entryDistance(
  tail: Spot,
  direction: Spot,
  length: number,
  rect: { left: number; top: number; right: number; bottom: number },
): number | null {
  const inside =
    tail.x >= rect.left &&
    tail.x <= rect.right &&
    tail.y >= rect.top &&
    tail.y <= rect.bottom;
  if (inside) return null;
  let enter = 0;
  let leave = length;
  for (const [start, step, low, high] of [
    [tail.x, direction.x, rect.left, rect.right],
    [tail.y, direction.y, rect.top, rect.bottom],
  ] as const) {
    if (step === 0) {
      if (start < low || start > high) return null;
      continue;
    }
    const near = ((step > 0 ? low : high) - start) / step;
    const far = ((step > 0 ? high : low) - start) / step;
    enter = Math.max(enter, near);
    leave = Math.min(leave, far);
  }
  return enter <= leave ? enter : null;
}

/**
 * The arrow of an Identify question (#73, FR-031): a box often holds other
 * things too, such as a desk's box holding the laptop on it, so a red arrow
 * edged in white points at the one object asked about, from a plain part of the
 * photo the server chose, with the question's number on its tail. Drawn as
 * shapes, it reads without colour (NFR-012).
 */
export function QuestionArrow({
  box,
  arrow,
  number,
}: {
  box: Box;
  arrow: Arrow;
  number: number;
}) {
  const frame = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });

  // The photo is sized by the screen, so the arrow is drawn again whenever it changes.
  useLayoutEffect(() => {
    const element = frame.current;
    if (!element) return;
    const measure = () =>
      setSize({ width: element.clientWidth, height: element.clientHeight });
    measure();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const geometry = arrowGeometry(box, arrow, size.width, size.height);
  const halfWidth = 8 * (geometry?.scale ?? 1);
  const head = geometry
    ? [
        geometry.tip,
        {
          x: geometry.headBase.x - Math.sin(geometry.angle) * halfWidth,
          y: geometry.headBase.y + Math.cos(geometry.angle) * halfWidth,
        },
        {
          x: geometry.headBase.x + Math.sin(geometry.angle) * halfWidth,
          y: geometry.headBase.y - Math.cos(geometry.angle) * halfWidth,
        },
      ]
        .map((spot) => `${spot.x},${spot.y}`)
        .join(' ')
    : '';
  const radius = geometry?.radius ?? BADGE_RADIUS.max;

  return (
    <div
      ref={frame}
      aria-hidden
      data-question-arrow
      className="pointer-events-none absolute inset-0"
    >
      {geometry ? (
        <svg
          width={size.width}
          height={size.height}
          className="absolute inset-0 overflow-visible"
        >
          <line
            x1={geometry.tail.x}
            y1={geometry.tail.y}
            x2={geometry.headBase.x}
            y2={geometry.headBase.y}
            stroke="white"
            strokeWidth={8 * geometry.scale}
            strokeLinecap="round"
          />
          <polygon
            points={head}
            fill="white"
            stroke="white"
            strokeWidth={5 * geometry.scale}
            strokeLinejoin="round"
          />
          <line
            x1={geometry.tail.x}
            y1={geometry.tail.y}
            x2={geometry.headBase.x}
            y2={geometry.headBase.y}
            stroke="var(--pointer)"
            strokeWidth={4 * geometry.scale}
            strokeLinecap="round"
          />
          <polygon points={head} fill="var(--pointer)" />
        </svg>
      ) : null}
      <span
        className="absolute grid -translate-x-1/2 -translate-y-1/2 place-items-center rounded-full border-white bg-pointer leading-none font-extrabold text-white shadow-[0_1px_4px_rgb(0_0_0/0.6)]"
        style={{
          // Where the arrow was drawn; before the photo has a size, where the server put it.
          left: geometry ? geometry.tail.x : `${arrow.tail.x * 100}%`,
          top: geometry ? geometry.tail.y : `${arrow.tail.y * 100}%`,
          width: radius * 2,
          height: radius * 2,
          borderWidth: radius >= 13 ? 3 : 2,
          fontSize: Math.round(radius * 1.05),
        }}
      >
        {number}
      </span>
    </div>
  );
}
