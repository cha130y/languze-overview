import type { Metadata } from 'next';
import Link from 'next/link';
import { ResendVerificationButton } from '@/components/auth/account-actions';
import { AiPaused } from '@/components/moderation/ai-paused';
import { TutorScene } from '@/components/scene/tutor-scene';
import { TutorChat } from '@/components/tutor/tutor-chat';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { buttonVariants } from '@/components/ui/button';
import { BackLink } from '@/components/layout/back-link';
import {
  getAccount,
  getTutorConversation,
  getUsage,
  requireLearner,
} from '@/lib/api/server';

export const metadata: Metadata = { title: 'ติวเตอร์ AI · LanguZe' };

/**
 * The AI tutor (US-070, US-073, US-080).
 *
 * The conversation is read on the server so that returning to the page shows what
 * was said last time without a wait (FR-070, V6). Everything after that is the
 * learner's own doing, and belongs to the client component that streams replies.
 *
 * An unverified learner is shown how to verify rather than a box that could only
 * be refused (FR-006). The API refuses them too, which is what actually enforces
 * the rule; this only avoids offering something that cannot work.
 *
 * Everything sits in the panel of Scene E3, the reading room; the panel keeps its
 * height and the conversation scrolls inside it, so the box to type in stays in view.
 */
export default async function TutorPage() {
  const [account, conversation, usage] = await Promise.all([
    getAccount(),
    getTutorConversation(),
    getUsage(),
  ]);

  requireLearner(account);

  const paused = account.aiAccess.reason === 'AI_SUSPENDED';

  return (
    <main className="desktop-readable flex w-full flex-1 flex-col">
      <TutorScene>
        <div className="shrink-0">
          <BackLink href="/" data-scene-leave>
            หน้าแรก
          </BackLink>
          <h1 className="mt-2 text-xl font-bold tracking-tight md:mt-3 md:text-2xl">
            ติวเตอร์ AI
          </h1>
          {/* Not while paused: it would promise exactly what the panel refuses. */}
          {paused ? null : (
            <p className="mt-1 text-sm text-muted-foreground max-md:hidden">
              ถามเรื่องภาษาอังกฤษได้
              ติวเตอร์ตอบเป็นภาษาไทยพร้อมตัวอย่างภาษาอังกฤษ
            </p>
          )}
        </div>

        {paused ? (
          <div className="min-h-0 flex-1 overflow-y-auto">
            <AiPaused access={account.aiAccess} />
          </div>
        ) : account.verifiedForAi ? (
          <TutorChat
            conversation={conversation?.messages ?? []}
            hasOlder={conversation?.nextCursor ?? null}
            usage={usage}
          />
        ) : (
          <div className="min-h-0 flex-1 overflow-y-auto">
            <Alert className="glass-panel rounded-2xl">
              <AlertTitle className="font-bold">
                ยืนยันอีเมลก่อนใช้ติวเตอร์ AI
              </AlertTitle>
              <AlertDescription className="grid gap-3">
                <span>
                  {account.email
                    ? `เราส่งลิงก์ยืนยันไปที่ ${account.email} แล้ว ติวเตอร์ AI จะใช้ได้หลังยืนยันอีเมล`
                    : 'ติวเตอร์ AI ใช้ได้หลังยืนยันบัญชีของคุณ'}
                </span>
                {account.email ? (
                  <ResendVerificationButton email={account.email} />
                ) : null}
                <Link
                  href="/account"
                  className={`${buttonVariants({ variant: 'outline' })} justify-self-start`}
                >
                  ไปที่บัญชีของฉัน
                </Link>
              </AlertDescription>
            </Alert>
          </div>
        )}
      </TutorScene>
    </main>
  );
}
