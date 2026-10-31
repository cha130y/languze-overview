import { API_BASE_URL, ApiError, errorFrom } from './client';

/**
 * Reading the tutor's reply as it is written (NFR-003, API design section 3.6).
 *
 * `EventSource` only opens event streams for GET requests, and sending a message
 * is a POST with a body, so the response is read with `fetch` and a stream reader
 * instead.
 */

/** One frame, before anything knows which event it is. */
export interface StreamFrame {
  event: string;
  data: unknown;
}

/** What the endpoint sends (API design, section 3.6). */
export type TutorStreamEvent =
  | { event: 'delta'; text: string }
  | { event: 'done'; messageId: string; replyId: string; messagesLeft: number }
  | { event: 'error'; code: string };

/**
 * The whole frames in a buffer, and whatever is left over.
 *
 * A chunk from the network is not a frame: one read can carry half a frame, or
 * three and a half. The remainder is handed back so it can wait for the rest
 * rather than being parsed as though it were complete.
 */
export function takeFrames(buffer: string): {
  frames: StreamFrame[];
  rest: string;
} {
  const pieces = buffer.split('\n\n');
  // The last piece has no blank line after it yet, so it may still be growing.
  const rest = pieces.pop() ?? '';

  return {
    frames: pieces
      .map(parseFrame)
      .filter((frame): frame is StreamFrame => frame !== null),
    rest,
  };
}

function parseFrame(text: string): StreamFrame | null {
  const event = /^event: (.*)$/m.exec(text)?.[1];
  const data = /^data: (.*)$/m.exec(text)?.[1];
  if (!event || data === undefined) return null;

  try {
    return { event, data: JSON.parse(data) };
  } catch {
    // Not something this app wrote; dropping it is better than failing the reply.
    return null;
  }
}

/** One frame as an event, or null for anything this app does not understand. */
export function asTutorEvent(frame: StreamFrame): TutorStreamEvent | null {
  const data = (frame.data ?? {}) as Record<string, unknown>;

  if (frame.event === 'delta' && typeof data.text === 'string') {
    return { event: 'delta', text: data.text };
  }
  if (
    frame.event === 'done' &&
    typeof data.messageId === 'string' &&
    typeof data.replyId === 'string' &&
    typeof data.messagesLeft === 'number'
  ) {
    return {
      event: 'done',
      messageId: data.messageId,
      replyId: data.replyId,
      messagesLeft: data.messagesLeft,
    };
  }
  if (frame.event === 'error' && typeof data.code === 'string') {
    return { event: 'error', code: data.code };
  }
  return null;
}

/**
 * Sends a message and gives back the reply as it arrives (FR-070, US-070).
 *
 * A refusal before the stream — no messages left, an unverified account, a message
 * too long — is an ordinary JSON error and is thrown, because nothing has been
 * answered. Once the stream is open the reply can only fail as an `error` event,
 * which is yielded like any other: by then the learner may already be reading.
 */
export async function* sendTutorMessage(
  content: string,
): AsyncIterable<TutorStreamEvent> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}/v1/tutor/messages`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content }),
    });
  } catch {
    throw new ApiError('NETWORK_ERROR', 0, 'The API could not be reached.');
  }

  if (!response.ok) {
    const body: unknown = await response.json().catch(() => undefined);
    throw errorFrom(body, response.status);
  }
  if (!response.body) {
    throw new ApiError(
      'SERVICE_UNAVAILABLE',
      response.status,
      'The reply carried no stream.',
    );
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;

    // `stream: true` keeps a Thai character split across two chunks readable.
    buffer += decoder.decode(value, { stream: true });

    const taken = takeFrames(buffer);
    buffer = taken.rest;
    for (const frame of taken.frames) {
      const event = asTutorEvent(frame);
      if (event) yield event;
    }
  }
}
