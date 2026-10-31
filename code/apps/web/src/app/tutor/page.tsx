import type { Metadata } from 'next';
import Link from 'next/link';
import { redirect } from 'next/navigation';
import { ResendVerificationButton } from '@/components/auth/account-actions';
import { AiPaused } from '@/components/moderation/ai-paused';
import { TutorChat } from '@/components/tutor/tutor-chat';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { buttonVariants } from '@/components/ui/button';
import { getAccount, getTutorConversation, getUsage } from '@/lib/api/server';

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
 */
export default async function TutorPage() {
  const [account, conversation, usage] = await Promise.all([
    getAccount(),
    getTutorConversation(),
    getUsage(),
  ]);

  if (!account) redirect('/sign-in');
  if (!account.termsAccepted) redirect('/terms');

  const paused = account.aiAccess.reason === 'AI_SUSPENDED';

  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-4 py-10">
      <div>
        <Link
          href="/"
          className="rounded text-sm text-muted-foreground underline-offset-4 outline-none hover:underline focus-visible:ring-3 focus-visible:ring-ring/50"
        >
          ← หน้าแรก
        </Link>
        <h1 className="mt-3 text-2xl font-bold tracking-tight">ติวเตอร์ AI</h1>
        {/* Not while paused: it would promise exactly what the panel refuses. */}
        {paused ? null : (
          <p className="mt-1 text-sm text-muted-foreground">
            ถามเรื่องภาษาอังกฤษได้ ติวเตอร์ตอบเป็นภาษาไทยพร้อมตัวอย่างภาษาอังกฤษ
          </p>
        )}
      </div>

      {paused ? (
        <AiPaused access={account.aiAccess} />
      ) : account.verifiedForAi ? (
        <TutorChat
          conversation={conversation?.messages ?? []}
          hasOlder={conversation?.nextCursor ?? null}
          usage={usage}
        />
      ) : (
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
      )}
    </main>
  );
}
