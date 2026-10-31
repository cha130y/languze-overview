import type { paths } from './schema';

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:4001';

type Json<T> = T extends { content: { 'application/json': infer B } }
  ? B
  : never;

export type Account = Json<paths['/v1/me']['get']['responses'][200]>;
export type SignUpBody = Json<paths['/v1/auth/sign-up']['post']['requestBody']>;
export type SignInBody = Json<paths['/v1/auth/sign-in']['post']['requestBody']>;
export type AcceptTermsBody = Json<
  paths['/v1/me/terms']['post']['requestBody']
>;

export type World = Json<
  paths['/v1/worlds/{worldId}']['get']['responses'][200]
>;
export type WorldSummary = Json<
  paths['/v1/worlds']['get']['responses'][200]
>[number];
export type WorldWord = World['words'][number];
export type WorldStatus = Json<
  paths['/v1/worlds/{worldId}/status']['get']['responses'][200]
>;
export type Usage = Json<paths['/v1/me/usage']['get']['responses'][200]>;
export type TutorConversation = Json<
  paths['/v1/tutor/messages']['get']['responses'][200]
>;
export type TutorMessage = TutorConversation['messages'][number];
export type Progress = Json<paths['/v1/progress']['get']['responses'][200]>;

export type PracticeSession = Json<
  paths['/v1/sessions/{sessionId}']['get']['responses'][200]
>;
export type Question = NonNullable<PracticeSession['nextQuestion']>;
export type SessionSummary = NonNullable<PracticeSession['summary']>;
export type AnswerResult = Json<
  paths['/v1/sessions/{sessionId}/questions/{questionId}/answer']['post']['responses'][200]
>;
export type AnswerBody = Json<
  paths['/v1/sessions/{sessionId}/questions/{questionId}/answer']['post']['requestBody']
>;

export type ReviewQueue = Json<
  paths['/v1/admin/review-queue']['get']['responses'][200]
>;
export type ReviewCase = ReviewQueue['cases'][number];
export type LearnerRecord = Json<
  paths['/v1/admin/learners/{learnerId}']['get']['responses'][200]
>;
export type AuditLog = Json<
  paths['/v1/admin/audit-log']['get']['responses'][200]
>;
export type AuditEntry = AuditLog['entries'][number];
export type AdminActionBody = Json<
  paths['/v1/admin/learners/{learnerId}/actions']['post']['requestBody']
>;
export type AdminAction = AdminActionBody['action'];

/** The providers LanguZe can offer (FR-003); the API says which are configured. */
export const PROVIDERS = ['google', 'line', 'facebook'] as const;
export type Provider = (typeof PROVIDERS)[number];

export const isProvider = (value: string): value is Provider =>
  (PROVIDERS as readonly string[]).includes(value);

/** The error shape every endpoint uses (API design, section 2.4). */
export interface ApiErrorBody {
  error: { code: string; message: string; details?: Record<string, unknown> };
}

export class ApiError extends Error {
  constructor(
    readonly code: string,
    readonly status: number,
    message: string,
    readonly details?: Record<string, unknown>,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

function isApiErrorBody(body: unknown): body is ApiErrorBody {
  return (
    typeof body === 'object' &&
    body !== null &&
    'error' in body &&
    typeof (body as ApiErrorBody).error?.code === 'string'
  );
}

/**
 * The error a failed response describes, or a generic one when it describes
 * nothing. Shared with the tutor stream, which reads its refusals the same way
 * but cannot go through `call` because its answer is a stream.
 */
export function errorFrom(body: unknown, status: number): ApiError {
  if (isApiErrorBody(body)) {
    return new ApiError(
      body.error.code,
      status,
      body.error.message,
      body.error.details,
    );
  }
  return new ApiError('INTERNAL_ERROR', status, 'The request failed.');
}

/**
 * Calls the API with the session cookie. The browser adds the `Origin` header,
 * which the API requires for requests that change data (API design, section 2.3).
 */
async function call<T>(
  path: string,
  // `json: false` is for a body the browser has to describe itself, such as an
  // upload, where naming the type by hand would leave out the multipart boundary.
  { json = true, ...init }: RequestInit & { json?: boolean } = {},
): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      credentials: 'include',
      headers: {
        ...(json ? { 'Content-Type': 'application/json' } : {}),
        ...init.headers,
      },
    });
  } catch {
    // No answer at all: the API is unreachable, or the network dropped.
    throw new ApiError('NETWORK_ERROR', 0, 'The API could not be reached.');
  }

  const body: unknown =
    response.status === 204
      ? undefined
      : await response.json().catch(() => undefined);

  if (!response.ok) throw errorFrom(body, response.status);

  return body as T;
}

export const api = {
  signUp: (body: SignUpBody) =>
    call<Account>('/v1/auth/sign-up', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  signIn: (body: SignInBody) =>
    call<Account>('/v1/auth/sign-in', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  signOut: () => call<void>('/v1/auth/sign-out', { method: 'POST' }),
  sendVerificationEmail: (email: string) =>
    call<{ ok: true }>('/v1/auth/send-verification-email', {
      method: 'POST',
      body: JSON.stringify({ email }),
    }),
  verifyEmail: (token: string) =>
    call<{ ok: true }>('/v1/auth/verify-email', {
      method: 'POST',
      body: JSON.stringify({ token }),
    }),
  requestPasswordReset: (email: string) =>
    call<{ ok: true }>('/v1/auth/request-password-reset', {
      method: 'POST',
      body: JSON.stringify({ email }),
    }),
  resetPassword: (token: string, newPassword: string) =>
    call<{ ok: true }>('/v1/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify({ token, newPassword }),
    }),
  me: () => call<Account>('/v1/me'),
  acceptTerms: (body: AcceptTermsBody) =>
    call<Account>('/v1/me/terms', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  declineTerms: () => call<void>('/v1/me/terms/decline', { method: 'POST' }),

  /**
   * Deletes the account and everything belonging to it (FR-007, US-009). The word
   * is the API's own guard against a stray request; the learner confirms on screen.
   */
  deleteAccount: () =>
    call<void>('/v1/me', {
      method: 'DELETE',
      body: JSON.stringify({ confirmation: 'DELETE' }),
    }),

  listWorlds: () => call<WorldSummary[]>('/v1/worlds'),
  getWorld: (worldId: string) => call<World>(`/v1/worlds/${worldId}`),
  renameWorld: (worldId: string, name: string) =>
    call<World>(`/v1/worlds/${worldId}`, {
      method: 'PATCH',
      body: JSON.stringify({ name }),
    }),
  deleteWorld: (worldId: string) =>
    call<void>(`/v1/worlds/${worldId}`, { method: 'DELETE' }),

  /** The status alone, which the waiting page asks for every 3 seconds (P3). */
  worldStatus: (worldId: string) =>
    call<WorldStatus>(`/v1/worlds/${worldId}/status`),

  /** Analyses the same photo again after a failure (FR-025, US-021). */
  retryAnalysis: (worldId: string) =>
    call<void>(`/v1/worlds/${worldId}/retry`, { method: 'POST' }),

  /** Starts a game for a world (FR-030, US-030). */
  startGame: (worldId: string) =>
    call<PracticeSession>('/v1/sessions', {
      method: 'POST',
      body: JSON.stringify({ kind: 'GAME', worldId }),
    }),

  /** Starts a review across every world (FR-050, US-050). */
  startReview: () =>
    call<PracticeSession>('/v1/sessions', {
      method: 'POST',
      body: JSON.stringify({ kind: 'REVIEW' }),
    }),

  /**
   * Answers one question (FR-032). The answer is checked on the server, which is
   * the only place that knows the word: it was never sent to the browser (S5).
   */
  answerQuestion: (sessionId: string, questionId: string, body: AnswerBody) =>
    call<AnswerResult>(
      `/v1/sessions/${sessionId}/questions/${questionId}/answer`,
      { method: 'POST', body: JSON.stringify(body) },
    ),

  /** A page of the conversation, newest first (FR-070). */
  tutorConversation: (before?: string) =>
    call<TutorConversation>(
      `/v1/tutor/messages${before ? `?before=${before}` : ''}`,
    ),

  /**
   * Throws the conversation away (FR-076, US-073). Today's messages are not given
   * back: they were sent, and forgetting them does not unsend them.
   */
  clearTutorConversation: () =>
    call<void>('/v1/tutor/messages', { method: 'DELETE' }),

  /**
   * Finds one learner by an exact address or an account ID (FR-108, US-106).
   *
   * Asked from the browser rather than through a page address, so an email address
   * an admin types never lands in the address bar or the browser's history: only
   * the account ID of whoever it finds does.
   */
  findLearner: (query: { email: string } | { accountId: string }) =>
    call<LearnerRecord>(
      `/v1/admin/learners?${new URLSearchParams(query).toString()}`,
    ),

  /** Acts on a learner's case, and returns the audit entry it made (FR-105, FR-106). */
  actOnLearner: (learnerId: string, body: AdminActionBody) =>
    call<AuditEntry>(`/v1/admin/learners/${learnerId}/actions`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  /** Removes one word the analysis got wrong (FR-026, US-022). */
  removeWord: (worldId: string, occurrenceId: string) =>
    call<void>(`/v1/worlds/${worldId}/words/${occurrenceId}`, {
      method: 'DELETE',
    }),

  /**
   * Creates a world from a photo (US-010). The body is `multipart/form-data`, so
   * the browser writes the content type itself — setting it by hand would leave out
   * the boundary and the API would read an empty body.
   */
  createWorld: (name: string, photo: File) => {
    const body = new FormData();
    body.set('name', name);
    body.set('photo', photo);
    return call<World>('/v1/worlds', { method: 'POST', body, json: false });
  },

  /**
   * Starts sign-in with a provider (FR-003). Better Auth serves this itself at
   * `/auth`, outside the `/v1` endpoints, so it answers with its own shape rather
   * than LanguZe's error contract and is called directly rather than through `call`.
   *
   * A first-time sign-up is sent to the Terms step and an existing account straight
   * home, which is `newUserCallbackURL` doing the work — the web app never has to
   * ask which case it is.
   */
  async startProviderSignIn(provider: Provider): Promise<string> {
    const origin = window.location.origin;
    let response: Response;
    try {
      response = await fetch(`${API_BASE_URL}/auth/sign-in/social`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          provider,
          callbackURL: `${origin}/`,
          newUserCallbackURL: `${origin}/terms`,
          // Better Auth appends its own `?error=<code>`, which the page reads.
          errorCallbackURL: `${origin}/sign-in`,
        }),
      });
    } catch {
      throw new ApiError('NETWORK_ERROR', 0, 'The API could not be reached.');
    }

    const body: unknown = await response.json().catch(() => undefined);
    const url =
      typeof body === 'object' && body !== null && 'url' in body
        ? (body as { url: unknown }).url
        : undefined;

    if (!response.ok || typeof url !== 'string') {
      throw new ApiError(
        'SERVICE_UNAVAILABLE',
        response.status,
        'Provider sign-in could not be started.',
      );
    }
    return url;
  },
};
