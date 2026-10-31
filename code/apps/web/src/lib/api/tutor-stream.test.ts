import { describe, expect, it } from 'vitest';
import { asTutorEvent, takeFrames } from './tutor-stream';

/** One frame as the API writes it (API design, section 3.6). */
const frame = (event: string, data: unknown) =>
  `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;

describe('pulling frames out of what the network gave us', () => {
  it('reads a whole frame', () => {
    const { frames, rest } = takeFrames(frame('delta', { text: 'คำนี้' }));

    expect(frames).toEqual([{ event: 'delta', data: { text: 'คำนี้' } }]);
    expect(rest).toBe('');
  });

  it('reads several frames that arrived together', () => {
    const buffer =
      frame('delta', { text: 'หนึ่ง' }) + frame('delta', { text: 'สอง' });

    const { frames } = takeFrames(buffer);

    expect(frames.map((one) => one.data)).toEqual([
      { text: 'หนึ่ง' },
      { text: 'สอง' },
    ]);
  });

  /*
   * The reason this function exists: a read can stop in the middle of a frame.
   * Parsing that half as though it were whole would lose the piece it carries.
   */
  it('keeps half a frame back until the rest of it arrives', () => {
    const whole = frame('delta', { text: 'คำนี้' });
    const cut = Math.floor(whole.length / 2);

    const first = takeFrames(whole.slice(0, cut));
    expect(first.frames).toEqual([]);

    const second = takeFrames(first.rest + whole.slice(cut));
    expect(second.frames).toEqual([
      { event: 'delta', data: { text: 'คำนี้' } },
    ]);
    expect(second.rest).toBe('');
  });

  it('keeps the tail when a frame and a half arrive', () => {
    const buffer =
      frame('delta', { text: 'หนึ่ง' }) + 'event: delta\ndata: {"text":"ส';

    const { frames, rest } = takeFrames(buffer);

    expect(frames).toHaveLength(1);
    expect(rest).toBe('event: delta\ndata: {"text":"ส');
  });

  it('has nothing to say about an empty buffer', () => {
    expect(takeFrames('')).toEqual({ frames: [], rest: '' });
  });

  /* Dropping a frame nobody wrote is better than failing a reply over it. */
  it('ignores a frame whose data is not JSON', () => {
    const { frames } = takeFrames('event: delta\ndata: not json\n\n');

    expect(frames).toEqual([]);
  });

  it('ignores a frame with no event name', () => {
    const { frames } = takeFrames('data: {"text":"คำนี้"}\n\n');

    expect(frames).toEqual([]);
  });
});

describe('what each frame means', () => {
  it('reads a piece of the reply', () => {
    expect(asTutorEvent({ event: 'delta', data: { text: 'คำนี้' } })).toEqual({
      event: 'delta',
      text: 'คำนี้',
    });
  });

  it('reads the end, with what was saved and what is left', () => {
    expect(
      asTutorEvent({
        event: 'done',
        data: { messageId: 'm', replyId: 'r', messagesLeft: 29 },
      }),
    ).toEqual({
      event: 'done',
      messageId: 'm',
      replyId: 'r',
      messagesLeft: 29,
    });
  });

  it('reads a failure as its code', () => {
    expect(
      asTutorEvent({
        event: 'error',
        data: { code: 'AI_PROVIDER_UNAVAILABLE' },
      }),
    ).toEqual({ event: 'error', code: 'AI_PROVIDER_UNAVAILABLE' });
  });

  /*
   * An event with the right name but the wrong shape is not usable. Returning
   * null keeps `undefined` out of the conversation, which would otherwise be
   * shown to the learner as part of the reply.
   */
  it('refuses an event that does not carry what its name promises', () => {
    expect(asTutorEvent({ event: 'delta', data: {} })).toBeNull();
    expect(asTutorEvent({ event: 'delta', data: { text: 7 } })).toBeNull();
    expect(
      asTutorEvent({ event: 'done', data: { messageId: 'm' } }),
    ).toBeNull();
    expect(
      asTutorEvent({
        event: 'done',
        data: { messageId: 'm', replyId: 'r', messagesLeft: 'lots' },
      }),
    ).toBeNull();
    expect(asTutorEvent({ event: 'error', data: {} })).toBeNull();
  });

  it('refuses an event it has never heard of', () => {
    expect(asTutorEvent({ event: 'ping', data: {} })).toBeNull();
  });

  it('copes with a frame carrying no data at all', () => {
    expect(asTutorEvent({ event: 'delta', data: null })).toBeNull();
  });
});
