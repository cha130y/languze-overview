import { Module } from '@nestjs/common';
import { ConfigService } from '@nestjs/config';
import type { EnvironmentVariables } from '../config/env.validation.js';
import { PrismaModule } from '../prisma/prisma.module.js';
import { GoogleGenAI } from '@google/genai';
import { Logger } from '@nestjs/common';
import { AiCallRecorder } from './ai-call-recorder.js';
import { AiProvider } from './ai-provider.js';
import { FakeAiProvider } from './fake-ai-provider.js';
import { GeminiAiProvider } from './gemini/gemini-ai-provider.js';

/**
 * The AI interface and the provider behind it (AIR-001, ADR-0004). Only this module
 * knows which provider answers; everything else depends on `AiProvider`.
 *
 * The fake provider is the default, so development and tests cost nothing and give
 * the same answer every time (B4). Gemini arrives as another adapter here.
 */
@Module({
  imports: [PrismaModule],
  providers: [
    AiCallRecorder,
    {
      provide: AiProvider,
      inject: [ConfigService],
      useFactory: (config: ConfigService<EnvironmentVariables, true>) => {
        const apiKey = config.get('GEMINI_API_KEY', { infer: true });
        const logger = new Logger('AiModule');

        if (apiKey) {
          logger.log('Photo analysis and the tutor use Gemini');
          return new GeminiAiProvider(new GoogleGenAI({ apiKey }).models, {
            safetyModel: config.get('GEMINI_SAFETY_MODEL', { infer: true }),
            extractionModel: config.get('GEMINI_EXTRACTION_MODEL', {
              infer: true,
            }),
            tutorModel: config.get('GEMINI_TUTOR_MODEL', { infer: true }),
            timeoutMs: config.get('AI_TIMEOUT_MS', { infer: true }),
            tutorTimeoutMs: config.get('AI_TUTOR_TIMEOUT_MS', { infer: true }),
            maxToolRounds: config.get('AI_TUTOR_MAX_TOOL_ROUNDS', {
              infer: true,
            }),
          });
        }

        const provider = new FakeAiProvider();
        // Lets a developer see a blocked photo or a provider error in the browser,
        // and take long enough over it to watch the page wait (FR-021).
        provider.behaviour = config.get('AI_FAKE_BEHAVIOUR', { infer: true });
        provider.delayMs = config.get('AI_FAKE_DELAY_MS', { infer: true });
        logger.log(
          `Photo analysis and the tutor use the fake provider (${provider.behaviour}, ${provider.delayMs} ms)`,
        );
        return provider;
      },
    },
  ],
  exports: [AiProvider, AiCallRecorder],
})
export class AiModule {}
