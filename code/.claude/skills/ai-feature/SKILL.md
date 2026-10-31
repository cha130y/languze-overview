---
name: ai-feature
description: Designs or implements a LanguZe AI feature with provider abstraction, schemas, prompt/model handling, validation, fallback behavior, and evaluation.
argument-hint: <ai-feature>
---

# AI Feature Workflow

1. Define product behavior and measurable success criteria.
2. Identify input/output schemas, authorization, and data-minimization requirements.
3. Trace the AI abstraction and provider adapters already in the repository.
4. Design deterministic behavior before writing the prompt.
5. Implement structured model output with runtime validation.
6. Add normalization, deduplication, policy checks, and persistence rules outside the model.
7. Add evaluation cases for happy path, ambiguity, malformed output, provider failure, and adversarial input where relevant.
8. Record provider/model assumptions, latency/cost implications, and fallback behavior.
9. Verify deterministic code with tests and model behavior with evaluation cases.
10. Update `docs/ai/` when the behavior or architecture is durable.

Never couple product business logic directly to one provider SDK. Never treat a plausible model response as proof of correctness.
