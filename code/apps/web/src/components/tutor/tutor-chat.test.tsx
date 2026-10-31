import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, test, vi } from 'vitest';
import { ApiError, api, type TutorMessage, type Usage } from '@/lib/api/client';
import { sendTutorMessage } from '@/lib/api/tutor-stream';
import type { TutorStreamEvent } from '@/lib/api/tutor-stream';
import { TutorChat } from './tutor-chat';

vi.mock('@/lib/api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/client')>()),
  api: { tutorConversation: vi.fn(), clearTutorConversation: vi.fn() },
}));

vi.mock('@/lib/api/tutor-stream', () => ({ sendTutorMessage: vi.fn() }));

const usage = (overrides: Partial<Usage> = {}): Usage =>
  ({
    analysesLeft: 10,
    analysesLimit: 10,
    messagesLeft: 30,
    messagesLimit: 30,
    resetsAt: '2026-10-02T17:00:00.000Z',
    ...overrides,
  }) as Usage;

const said = (
  id: string,
  role: TutorMessage['role'],
  content: string,
): TutorMessage => ({
  id,
  role,
  content,
  createdAt: '2026-10-02T04:00:00.000Z',
});

/** A reply that arrives in the pieces the test names. */
function streams(...events: TutorStreamEvent[]) {
  vi.mocked(sendTutorMessage).mockImplementation(async function* () {
    for (const event of events) yield event;
  });
}

const done = (messagesLeft = 29): TutorStreamEvent => ({
  event: 'done',
  messageId: 'message-new',
  replyId: 'reply-new',
  messagesLeft,
});

const show = (
  conversation: TutorMessage[] = [],
  options: { usage?: Usage | null; hasOlder?: string | null } = {},
) =>
  render(
    <TutorChat
      conversation={conversation}
      usage={options.usage === undefined ? usage() : options.usage}
      hasOlder={options.hasOlder ?? null}
    />,
  );

/** jsdom has no layout, so what the page scrolls to is recorded instead. */
const scrolledTo: Element[] = [];

const box = () => screen.getByLabelText('ข้อความถึงติวเตอร์');
const sendButton = () => screen.getByRole('button', { name: 'ส่ง' });

beforeEach(() => {
  vi.clearAllMocks();
  // jsdom has no layout, so this is never meaningful here.
  scrolledTo.length = 0;
  Element.prototype.scrollIntoView = function scrollIntoView(this: Element) {
    scrolledTo.push(this);
  };
});

describe('what is already there (US-070 criterion 4)', () => {
  /* The API answers newest first; a conversation is read the other way round. */
  test('shows the conversation oldest first', () => {
    show([
      said('2', 'TUTOR', 'คำนี้แปลว่าโซฟา'),
      said('1', 'LEARNER', 'sofa แปลว่าอะไร'),
    ]);

    const shown = screen
      .getAllByText(/sofa แปลว่าอะไร|คำนี้แปลว่าโซฟา/)
      .map((node) => node.textContent);
    expect(shown).toEqual(['sofa แปลว่าอะไร', 'คำนี้แปลว่าโซฟา']);
  });

  /* A blank box is a poor welcome, so a first visit is given somewhere to start. */
  test('offers something to ask when there is nothing yet', async () => {
    const user = userEvent.setup();
    show([]);

    expect(screen.getByText('ถามติวเตอร์ได้เลย')).toBeInTheDocument();
    await user.click(
      screen.getByRole('button', { name: 'ทำไมฉันจำคำศัพท์พวกนี้ไม่ได้' }),
    );

    expect(box()).toHaveValue('ทำไมฉันจำคำศัพท์พวกนี้ไม่ได้');
  });

  test('offers older messages only when there are some', () => {
    const { unmount } = show([said('1', 'LEARNER', 'สวัสดี')]);
    expect(
      screen.queryByRole('button', { name: 'ดูข้อความก่อนหน้า' }),
    ).not.toBeInTheDocument();
    unmount();

    show([said('1', 'LEARNER', 'สวัสดี')], { hasOlder: 'message-0' });
    expect(
      screen.getByRole('button', { name: 'ดูข้อความก่อนหน้า' }),
    ).toBeInTheDocument();
  });

  test('puts older messages above the ones already shown', async () => {
    const user = userEvent.setup();
    vi.mocked(api.tutorConversation).mockResolvedValue({
      messages: [said('0', 'LEARNER', 'คำถามเก่า')],
      nextCursor: null,
    });
    show([said('1', 'LEARNER', 'คำถามใหม่')], { hasOlder: 'message-1' });

    await user.click(screen.getByRole('button', { name: 'ดูข้อความก่อนหน้า' }));

    await waitFor(() =>
      expect(screen.getByText('คำถามเก่า')).toBeInTheDocument(),
    );
    const shown = screen
      .getAllByText(/คำถามเก่า|คำถามใหม่/)
      .map((node) => node.textContent);
    expect(shown).toEqual(['คำถามเก่า', 'คำถามใหม่']);
    // The cursor is spent, so the button goes.
    expect(
      screen.queryByRole('button', { name: 'ดูข้อความก่อนหน้า' }),
    ).not.toBeInTheDocument();
  });
});

describe('asking something (US-070 criterion 1)', () => {
  test('shows the reply as it is written, then keeps it', async () => {
    const user = userEvent.setup();
    streams(
      { event: 'delta', text: 'คำนี้' },
      { event: 'delta', text: 'ใช้บอกสิ่งของ' },
      done(),
    );
    show([]);

    await user.type(box(), 'sofa แปลว่าอะไร');
    await user.click(sendButton());

    await waitFor(() =>
      expect(screen.getByText('คำนี้ใช้บอกสิ่งของ')).toBeInTheDocument(),
    );
    expect(sendTutorMessage).toHaveBeenCalledWith('sofa แปลว่าอะไร');
    // Both sides of the exchange are now part of the conversation.
    expect(screen.getByText('sofa แปลว่าอะไร')).toBeInTheDocument();
    expect(box()).toHaveValue('');
  });

  /* US-080: the count comes from the reply, so it needs no second request. */
  test('counts down what is left today', async () => {
    const user = userEvent.setup();
    streams({ event: 'delta', text: 'ค่ะ' }, done(27));
    show([]);

    expect(screen.getByText('เหลืออีก 30 ข้อความวันนี้')).toBeInTheDocument();
    await user.type(box(), 'สวัสดี');
    await user.click(sendButton());

    await waitFor(() =>
      expect(screen.getByText('เหลืออีก 27 ข้อความวันนี้')).toBeInTheDocument(),
    );
  });

  /*
   * The box has to stay in view. Scrolling to a mark above it would put the
   * newest reply at the bottom of the screen and the box below that, so a
   * learner with a long conversation would scroll back up to type again.
   */
  test('keeps the box in view, not just the newest message', async () => {
    const user = userEvent.setup();
    streams({ event: 'delta', text: 'ค่ะ' }, done());
    const { container } = show([]);

    await user.type(box(), 'สวัสดี');
    await user.click(sendButton());

    await waitFor(() => expect(scrolledTo.length).toBeGreaterThan(0));
    const form = container.querySelector('form');
    const anchor = scrolledTo.at(-1)!;
    expect(form).not.toBeNull();
    expect(
      form!.compareDocumentPosition(anchor) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  test('will not send an empty message', async () => {
    const user = userEvent.setup();
    show([]);

    expect(sendButton()).toBeDisabled();
    await user.type(box(), '   ');

    expect(sendButton()).toBeDisabled();
    expect(sendTutorMessage).not.toHaveBeenCalled();
  });

  /* US-080 criterion 4: the box stops at 1,000, and says so near the end. */
  test('shows the length limit only near it, and stops at it', async () => {
    const user = userEvent.setup();
    show([]);

    await user.click(box());
    await user.paste('a'.repeat(500));
    expect(screen.queryByText(/ยาวได้ไม่เกิน 1,000 ตัวอักษร/)).toBeNull();

    await user.paste('a'.repeat(700));

    expect(box()).toHaveValue('a'.repeat(1000));
    expect(
      screen.getByText(
        'ข้อความหนึ่งยาวได้ไม่เกิน 1,000 ตัวอักษร (ตอนนี้ 1,000)',
      ),
    ).toBeInTheDocument();
    expect(box()).toHaveAccessibleDescription(/ยาวได้ไม่เกิน 1,000/);
  });

  test('sends on Enter and writes a new line on Shift+Enter', async () => {
    const user = userEvent.setup();
    streams({ event: 'delta', text: 'ค่ะ' }, done());
    show([]);

    await user.type(box(), 'บรรทัดหนึ่ง{Shift>}{Enter}{/Shift}บรรทัดสอง');
    expect(sendTutorMessage).not.toHaveBeenCalled();

    await user.type(box(), '{Enter}');
    await waitFor(() =>
      expect(sendTutorMessage).toHaveBeenCalledWith('บรรทัดหนึ่ง\nบรรทัดสอง'),
    );
  });
});

describe('when the reply fails (US-070 criterion 5)', () => {
  /*
   * Nothing was saved and nothing was counted, so the message is handed back
   * rather than lost: typing it again is the one thing a learner should not
   * have to do (FR-071).
   */
  test('says so in Thai and gives the message back', async () => {
    const user = userEvent.setup();
    streams(
      { event: 'delta', text: 'เริ่ม' },
      { event: 'error', code: 'AI_PROVIDER_UNAVAILABLE' },
    );
    show([]);

    await user.type(box(), 'ช่วยอธิบายหน่อย');
    await user.click(sendButton());

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(
        'ระบบ AI ไม่พร้อมใช้งานชั่วคราว',
      ),
    );
    expect(box()).toHaveValue('ช่วยอธิบายหน่อย');
    // The half-written reply is gone, because none of it was kept.
    expect(screen.queryByText('เริ่ม')).not.toBeInTheDocument();
  });

  test('reports a refusal that came before the stream', async () => {
    const user = userEvent.setup();
    vi.mocked(sendTutorMessage).mockImplementation(() => {
      throw new ApiError('DAILY_TUTOR_LIMIT', 429, 'None left today.');
    });
    show([]);

    await user.type(box(), 'สวัสดี');
    await user.click(sendButton());

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('ติวเตอร์ครบจำนวน'),
    );
    expect(box()).toHaveValue('สวัสดี');
  });
});

describe('with no messages left today (FR-071)', () => {
  it('closes the box and says when it opens again', () => {
    show([], { usage: usage({ messagesLeft: 0 }) });

    expect(
      screen.queryByLabelText('ข้อความถึงติวเตอร์'),
    ).not.toBeInTheDocument();
    expect(screen.getByText(/เริ่มนับใหม่/)).toBeInTheDocument();
    expect(screen.getByText(/ครบ 30 ข้อความแล้ว/)).toBeInTheDocument();
  });

  /* The API could not say, which must not be read as "none left". */
  it('still offers the box when the count is unknown', () => {
    show([], { usage: null });

    expect(box()).toBeInTheDocument();
    expect(screen.queryByText(/เหลืออีก/)).not.toBeInTheDocument();
  });
});

describe('clearing it (US-073)', () => {
  test('asks first, and says what clearing does not undo', async () => {
    const user = userEvent.setup();
    show([said('1', 'LEARNER', 'สวัสดี')]);

    await user.click(screen.getByRole('button', { name: 'ล้างบทสนทนา' }));

    expect(
      screen.getByText(/จำนวนข้อความที่ใช้ไปวันนี้จะไม่คืนให้/),
    ).toBeInTheDocument();
    expect(api.clearTutorConversation).not.toHaveBeenCalled();
  });

  test('empties the conversation once confirmed', async () => {
    const user = userEvent.setup();
    vi.mocked(api.clearTutorConversation).mockResolvedValue(undefined);
    show([said('1', 'LEARNER', 'สวัสดี')]);

    await user.click(screen.getByRole('button', { name: 'ล้างบทสนทนา' }));
    await user.click(
      screen.getAllByRole('button', { name: 'ล้างบทสนทนา' }).at(-1)!,
    );

    await waitFor(() =>
      expect(screen.queryByText('สวัสดี')).not.toBeInTheDocument(),
    );
    expect(screen.getByText('ถามติวเตอร์ได้เลย')).toBeInTheDocument();
  });

  test('keeps the conversation when clearing fails', async () => {
    const user = userEvent.setup();
    vi.mocked(api.clearTutorConversation).mockRejectedValue(
      new ApiError('NETWORK_ERROR', 0, 'unreachable'),
    );
    show([said('1', 'LEARNER', 'สวัสดี')]);

    await user.click(screen.getByRole('button', { name: 'ล้างบทสนทนา' }));
    await user.click(
      screen.getAllByRole('button', { name: 'ล้างบทสนทนา' }).at(-1)!,
    );

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(screen.getByText('สวัสดี')).toBeInTheDocument();
  });

  test('offers nothing to clear when there is nothing', () => {
    show([]);

    expect(
      screen.queryByRole('button', { name: 'ล้างบทสนทนา' }),
    ).not.toBeInTheDocument();
  });
});
