# LanguZe: an overview

> **Learn from your world.**

LanguZe (pronounced lang-u-zay) is an AI-powered, gamified English-learning web app for Thai adults. Learners turn photos of their world into English vocabulary, play games with those words, review mistakes and ask an AI tutor questions grounded in their own progress.

The app is public at [languze.com](https://languze.com). The source repository remains private; this public overview gives a guided tour of the product, architecture and selected code.

## Status

- **Release 1.0 launched on 2026-10-27** and opened to the public at [languze.com](https://languze.com) on 2026-11-24.
- **The production API has run on ASP.NET Core since 2026-11-06.** It was ported from NestJS one slice per pull request and verified with black-box contract tests before cutover. The NestJS implementation has since been removed from the private repository; its history remains in Git.

## The app

[Watch the 1:44 LanguZe introduction](media/Languze-introduction.mp4) (1080p MP4, 50.5 MB).

![Current LanguZe home screen, with navigation for photo capture, worlds, review, the AI tutor and progress](screenshots/09-home-current.png)

The screenshot was captured from the live app on 2026-11-25. The interface is in Thai, as the learners are (NFR-013).

## Where to look

A few files show most of the work. Paths are under [`code/`](code/), which keeps the original repository's layout.

**Current code.** The curated selection follows the production ASP.NET Core API and Next.js app. Read the API slices, frontend flow and contract suite to see how the product is built; the historical pull requests explain how the API port was verified. The NestJS implementation has been removed from the private repository.

**Full-stack**

- [`world-photo.tsx`](code/apps/web/src/components/worlds/world-photo.tsx) and [`world-words.tsx`](code/apps/web/src/components/worlds/world-words.tsx): the boxes drawn on a learner's photo and the words beside them, with their tests
- [`create-world-form.tsx`](code/apps/web/src/components/worlds/create-world-form.tsx) and [`analysis-watcher.tsx`](code/apps/web/src/components/worlds/analysis-watcher.tsx): uploading a photo, then watching the background analysis finish
- [Progress page](code/apps/web/src/app/progress/) and [review page](code/apps/web/src/app/review/): how mistakes lead into continued practice.
- [`tutor-stream.ts`](code/apps/web/src/lib/api/tutor-stream.ts): the tutor's streamed reply, read as it arrives
- [Playwright smoke tests](code/apps/web/e2e/) of worlds, the game and the tutor, run in CI against the API with the fake AI provider
- [Accessibility review](code/docs/testing/accessibility-review.md) (WCAG 2.2 AA) and [performance review](code/docs/testing/performance-review.md): non-AI requests at p95 25 ms under a k6 load test

**AI engineering**

- [GeminiAiProvider.cs](code/apps/api-dotnet/LanguZe.Api/Modules/Ai/Shared/Gemini/GeminiAiProvider.cs) is the only code that knows Gemini's SDK, behind a provider interface. A safety check runs before extraction, the model's refusal counts as a block, and structured output is parsed as untrusted.
- [ExtractionRules.cs](code/apps/api-dotnet/LanguZe.Api/Modules/Vocabulary/Contracts/ExtractionRules.cs) defines what an extracted word must satisfy before it is saved.
- [GeminiAiProviderTests.cs](code/apps/api-dotnet/LanguZe.Api.Tests/Ai/GeminiAiProviderTests.cs) covers provider failures and pins prompts by SHA-256.
- [AI evaluation](code/docs/ai/evaluation.md), with photo and tutor cases in the current .NET API, records the model selection and evaluation results ([ADR-0004](code/docs/architecture/adr/0004-ai-provider.md)).
- [SendMessageHandler.cs](code/apps/api-dotnet/LanguZe.Api/Modules/Tutor/Features/SendMessage/SendMessageHandler.cs) and [TutorTools.cs](code/apps/api-dotnet/LanguZe.Api/Modules/Tutor/Shared/TutorTools.cs) implement the tutor reply and read-only tools, grounded in the learner's history.

**AI-assisted development (AI SDLC)**

The work used Claude Code inside a harness that makes AI changes checkable:

- The [rules](code/.claude/rules/) and [agents](code/.claude/agents/) define what the AI must and must not do, such as reproducing NestJS's behaviour during the port rather than improving it
- [feature delivery workflow](code/.claude/skills/feature/SKILL.md) and [AI feature workflow](code/.claude/skills/ai-feature/SKILL.md) define repeatable steps for feature changes and AI evaluation.
- [`settings.json`](code/.claude/settings.json) and its [hooks](code/.claude/scripts/): environment files the AI cannot read, and formatting run after every edit
- The [pull requests](pull-requests/README.md), five from building Release 1.0 and three from the port: each has its own verification, and those from the port also say what the checks caught, including mistakes the AI made and the test that found them

**Backend and architecture**

- [`DependencyRules.cs`](code/apps/api-dotnet/LanguZe.Api.Tests/Architecture/DependencyRules.cs): architecture tests that fail the build when an endpoint touches the database or a module bypasses another's contracts
- [`AnalysisWork.cs`](code/apps/api-dotnet/LanguZe.Api/Modules/Worlds/Shared/AnalysisWork.cs): one analysis, where each ending (words, a block and the pause it earns, a failure) is one transaction
- [`StaleAnalysis.cs`](code/apps/api-dotnet/LanguZe.Api/Modules/Worlds/Jobs/StaleAnalysis.cs): releases analyses lost to a restart, reading the database only while one may be running, so the serverless database can sleep
- The [contract suite](code/tests/api-contract/) and [COVERAGE.md](code/tests/api-contract/COVERAGE.md) document API behavior held through the port; the current API is guarded against the committed OpenAPI contract.
- [Integration tests](code/apps/api-dotnet/LanguZe.Api.IntegrationTests/) on throwaway databases, with a clock the test moves

**Requirements and design**

The [PRD](code/docs/requirements/PRD.md), the [SRS](code/docs/requirements/SRS.md) with numbered requirements that the code and tests cite, the [architecture overview](code/docs/architecture/overview.md), the [data model](code/docs/architecture/data-model.md), the [API design](code/docs/architecture/api-design.md), the [process flows](code/docs/flows/README.md) and [seven ADRs](code/docs/architecture/adr/).

## Engineering practices

- **Requirements to code.** The documents came before the code; comments and tests name the requirement they serve, such as `FR-092` or `V15`.
- **AI behind an interface.** A fake provider makes development and tests free and repeatable, and can be told to block, fail, time out or return bad output, so every failure path is tested. The learner's identity never reaches the provider.
- **Tests that are tested.** Each ported slice is checked with deliberate mutations, such as running two checks in the wrong order, dropping a cleanup record or loosening a rule. Its pull request reports which test caught each one.
- **Strict builds.** Warnings are errors, formatting is checked in CI, and package versions are pinned centrally with lock files.

## Tech stack

| Area           | Technology                                                                                                 |
| -------------- | ---------------------------------------------------------------------------------------------------------- |
| Web            | Next.js 16 (App Router), React 19, TypeScript 6, Tailwind CSS 4, shadcn/ui, React Hook Form + Zod          |
| API            | .NET 10, ASP.NET Core Minimal APIs, EF Core 10 with Npgsql, ASP.NET Core Identity, Google.GenAI, NetVips |
| Data           | PostgreSQL 18 (Neon in production), S3-compatible photo storage (Cloudflare R2 in production; SeaweedFS locally)                                 |
| AI             | Google Gemini behind a provider interface, structured output, a fake provider                              |
| Testing        | Vitest, Playwright, xUnit v3, ArchUnitNET, k6                                                   |
| Delivery       | pnpm workspace, GitHub Actions, Docker, Railway, Cloudflare, Resend                                        |

## About this selection

- The code selection is synced from LanguZe main at commit eb7bb72 (2026-11-26). Pull-request notes remain historical examples. The curated selection does not build on its own.
- [`tools/sync.mjs`](tools/sync.mjs) copies the files in [`tools/manifest.json`](tools/manifest.json) from the private repository, and only files git tracks there.

## Licence

Shared for review; all rights reserved. No licence is granted to use, copy, modify or distribute this code.
