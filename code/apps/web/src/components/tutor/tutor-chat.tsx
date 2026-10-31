'use client';

import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { api, type TutorMessage, type Usage } from '@/lib/api/client';
import { messageForCode, messageForError } from '@/lib/api/error-messages';
import { sendTutorMessage } from '@/lib/api/tutor-stream';
import { resetsAtText } from '@/lib/daily-limit';
import { useTypedBeforeHydration } from '@/lib/use-typed-before-hydration';

/** The longest message the API accepts (V7, FR-071); the box says so too. */
const MAX_MESSAGE = 1000;

/* The length is shown only near the limit, where it matters (US-080 criterion 4). */
const NEAR_LIMIT = MAX_MESSAGE - 100;

/** Questions worth asking, for a learner facing an empty box for the first time. */
const EXAMPLES = [
  'ทำไมฉันจำคำศัพท์พวกนี้ไม่ได้',
  'ช่วยอธิบายคำที่ฉันตอบผิดบ่อยที่สุด',
  'ขอประโยคตัวอย่างของคำที่ฉันเพิ่งเรียน',
];

/**
 * The conversation with the AI tutor (US-070, US-073, US-080).
 *
 * The reply is streamed, so the learner watches it being written rather than a
 * spinner (NFR-003). Nothing is saved until it is whole, which is why a reply
 * that fails leaves the conversation exactly as it was and hands the message
 * back for another try (FR-071).
 *
 * This component owns the whole conversation, including clearing it. Keeping the
 * clear button outside would mean refreshing the page to empty a list this
 * component holds in state — and client state survives a refresh.
 */
export function TutorChat({
  conversation,
  usage,
  hasOlder,
}: {
  conversation: TutorMessage[];
  usage: Usage | null;
  hasOlder: string | null;
}) {
  // Oldest first, as a conversation is read; the API answers newest first.
  const [messages, setMessages] = useState<TutorMessage[]>(() =>
    [...conversation].reverse(),
  );
  const [older, setOlder] = useState(hasOlder);
  const [draft, setDraft] = useState('');
  const [reply, setReply] = useState('');
  const [sending, setSending] = useState(false);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [messagesLeft, setMessagesLeft] = useState(usage?.messagesLeft ?? null);

  const foot = useRef<HTMLDivElement>(null);
  const box = useRef<HTMLTextAreaElement>(null);
  useTypedBeforeHydration(box, setDraft);

  /*
   * Keep the newest in view: on arrival the last thing said is what a learner
   * wants, and while a reply is being written it is what they are reading.
   */
  useEffect(() => {
    foot.current?.scrollIntoView({ block: 'end' });
  }, [messages.length, reply]);

  const outOfMessages = messagesLeft === 0;

  async function send(content: string) {
    const asked = content.trim();
    if (!asked || sending) return;

    setSending(true);
    setError(null);
    setDraft('');
    setReply('');

    let streamed = '';
    try {
      for await (const event of sendTutorMessage(asked)) {
        if (event.event === 'delta') {
          streamed += event.text;
          setReply(streamed);
          continue;
        }
        if (event.event === 'error') {
          throw new StreamFailed(event.code);
        }

        // Saved: the streamed text becomes the two messages the server kept.
        setMessages((kept) => [
          ...kept,
          {
            id: event.messageId,
            role: 'LEARNER',
            content: asked,
            createdAt: now(),
          },
          {
            id: event.replyId,
            role: 'TUTOR',
            content: streamed,
            createdAt: now(),
          },
        ]);
        setMessagesLeft(event.messagesLeft);
        setReply('');
      }
    } catch (caught) {
      /*
       * Nothing was saved and the message was not counted, so the only kind thing
       * to do is give the text back rather than make them type it again (FR-071).
       */
      setError(
        caught instanceof StreamFailed
          ? messageForCode(caught.code)
          : messageForError(caught),
      );
      setDraft(asked);
      setReply('');
    } finally {
      setSending(false);
    }
  }

  async function loadOlder() {
    if (!older || loadingOlder) return;
    setLoadingOlder(true);
    setError(null);
    try {
      const page = await api.tutorConversation(older);
      setMessages((kept) => [...[...page.messages].reverse(), ...kept]);
      setOlder(page.nextCursor ?? null);
    } catch (caught) {
      setError(messageForError(caught));
    } finally {
      setLoadingOlder(false);
    }
  }

  async function clear() {
    setClearing(true);
    setError(null);
    try {
      await api.clearTutorConversation();
      setMessages([]);
      setOlder(null);
      setReply('');
    } catch (caught) {
      setError(messageForError(caught));
    } finally {
      setClearing(false);
    }
  }

  return (
    <div className="grid gap-4">
      {messages.length > 0 ? (
        <ClearConversation onClear={clear} clearing={clearing} />
      ) : null}

      {older ? (
        <Button
          variant="outline"
          size="sm"
          onClick={loadOlder}
          disabled={loadingOlder}
          className="justify-self-center"
        >
          {loadingOlder ? 'กำลังโหลด…' : 'ดูข้อความก่อนหน้า'}
        </Button>
      ) : null}

      <div
        className="grid gap-3"
        aria-live="polite"
        aria-busy={sending}
        aria-label="บทสนทนากับติวเตอร์"
      >
        {messages.length === 0 && !sending ? (
          <section className="glass-panel grid gap-3 rounded-3xl p-6">
            <h2 className="font-bold">ถามติวเตอร์ได้เลย</h2>
            <p className="text-sm text-muted-foreground">
              ติวเตอร์เห็นคำศัพท์และข้อที่คุณตอบผิดของคุณเอง
              จึงอธิบายให้ตรงกับที่คุณกำลังเรียนได้ ตอบเป็นภาษาไทย
            </p>
            <ul className="grid gap-2">
              {EXAMPLES.map((example) => (
                <li key={example}>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={outOfMessages}
                    onClick={() => {
                      setDraft(example);
                      box.current?.focus();
                    }}
                    className="h-auto w-full justify-start whitespace-normal py-2 text-left"
                  >
                    {example}
                  </Button>
                </li>
              ))}
            </ul>
          </section>
        ) : null}

        {messages.map((message) => (
          <Bubble key={message.id} role={message.role}>
            {message.content}
          </Bubble>
        ))}

        {/* The reply as it is written; it becomes a saved message once whole. */}
        {sending ? (
          <Bubble role="TUTOR">
            {reply === '' ? (
              <span className="text-muted-foreground">กำลังคิด…</span>
            ) : (
              reply
            )}
          </Bubble>
        ) : null}
      </div>

      {error ? (
        <p
          role="alert"
          className="rounded-2xl bg-destructive/10 px-4 py-3 text-sm text-destructive"
        >
          {error}
        </p>
      ) : null}

      {/* FR-071: with none left the box is closed, and says when it opens again. */}
      {outOfMessages && usage ? (
        <p className="glass-panel rounded-2xl p-4 text-sm text-muted-foreground">
          วันนี้คุณคุยกับติวเตอร์ครบ {usage.messagesLimit} ข้อความแล้ว
          เริ่มนับใหม่ {resetsAtText(usage.resetsAt)} น. ตามเวลาไทย
        </p>
      ) : (
        <form
          onSubmit={(submit) => {
            submit.preventDefault();
            void send(draft);
          }}
          className="grid gap-2"
        >
          <label htmlFor="tutor-message" className="sr-only">
            ข้อความถึงติวเตอร์
          </label>
          <textarea
            id="tutor-message"
            ref={box}
            value={draft}
            onChange={(change) => setDraft(change.target.value)}
            onKeyDown={(key) => {
              // Enter sends; Shift+Enter writes another line, as a chat box does.
              if (key.key === 'Enter' && !key.shiftKey) {
                key.preventDefault();
                void send(draft);
              }
            }}
            maxLength={MAX_MESSAGE}
            aria-describedby={
              draft.length > NEAR_LIMIT ? 'tutor-message-length' : undefined
            }
            rows={3}
            disabled={sending}
            placeholder="พิมพ์คำถามเกี่ยวกับภาษาอังกฤษ"
            className="w-full resize-y rounded-2xl border border-border bg-background px-4 py-3 text-base outline-none focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-60 dark:border-input dark:bg-input/30"
          />
          {/* The box stops at the limit, and a longer paste is cut: this says why. */}
          {draft.length > NEAR_LIMIT ? (
            <p
              id="tutor-message-length"
              className="text-xs text-muted-foreground"
            >
              ข้อความหนึ่งยาวได้ไม่เกิน {MAX_MESSAGE.toLocaleString('en-US')}{' '}
              ตัวอักษร (ตอนนี้ {draft.length.toLocaleString('en-US')})
            </p>
          ) : null}
          <div className="flex items-center justify-between gap-3">
            <span className="text-xs text-muted-foreground">
              {messagesLeft === null
                ? null
                : `เหลืออีก ${messagesLeft} ข้อความวันนี้`}
            </span>
            <Button
              type="submit"
              variant="cta"
              size="xl"
              disabled={sending || draft.trim() === ''}
            >
              {sending ? 'กำลังตอบ…' : 'ส่ง'}
            </Button>
          </div>
        </form>
      )}

      {/*
       * Below the box, not above it. Scrolling to a mark that sits under the last
       * message would push the box itself off the bottom of the screen, so a
       * learner with a long conversation would have to scroll back to type.
       */}
      <div ref={foot} />
    </div>
  );
}

/** Carries the code an `error` event named, so the Thai text can be chosen. */
class StreamFailed extends Error {
  constructor(readonly code: string) {
    super(`The tutor reply failed: ${code}`);
  }
}

/**
 * Clearing is permanent, so it asks first (FR-076, US-073). The warning says what
 * clearing does not do, because a learner may reasonably expect it to give the
 * day's messages back.
 */
function ClearConversation({
  onClear,
  clearing,
}: {
  onClear: () => Promise<void>;
  clearing: boolean;
}) {
  const [asking, setAsking] = useState(false);

  if (!asking) {
    return (
      <Button
        variant="ghost"
        size="sm"
        onClick={() => setAsking(true)}
        className="justify-self-end text-muted-foreground"
      >
        ล้างบทสนทนา
      </Button>
    );
  }

  return (
    <section className="glass-panel grid gap-3 rounded-2xl p-4">
      <p className="text-sm">
        ล้างบทสนทนาทั้งหมดและกู้คืนไม่ได้ จำนวนข้อความที่ใช้ไปวันนี้จะไม่คืนให้
      </p>
      <div className="flex gap-2">
        <Button
          variant="destructive"
          onClick={() => void onClear()}
          disabled={clearing}
        >
          {clearing ? 'กำลังล้าง…' : 'ล้างบทสนทนา'}
        </Button>
        <Button
          variant="outline"
          onClick={() => setAsking(false)}
          disabled={clearing}
        >
          ยกเลิก
        </Button>
      </div>
    </section>
  );
}

function Bubble({
  role,
  children,
}: {
  role: TutorMessage['role'];
  children: React.ReactNode;
}) {
  const mine = role === 'LEARNER';

  return (
    <div className={mine ? 'flex justify-end' : 'flex justify-start'}>
      <p
        className={`max-w-[85%] rounded-2xl px-4 py-3 text-sm whitespace-pre-wrap ${
          mine
            ? 'bg-primary text-primary-foreground'
            : 'glass-panel text-foreground'
        }`}
      >
        {children}
      </p>
    </div>
  );
}

/** The time a message was written, for a row the server has not sent back yet. */
const now = () => new Date().toISOString();
