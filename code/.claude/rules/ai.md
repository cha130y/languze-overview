---
paths:
  - "apps/api/**/*ai*/**/*.{ts,tsx}"
  - "apps/api/**/*tutor*/**/*.{ts,tsx}"
  - "packages/ai/**/*.{ts,tsx}"
  - "docs/ai/**/*.md"
---

# AI Engineering Rules

- Business logic must depend on an application-level AI interface, not directly on Gemini/OpenAI/Claude SDKs.
- Treat model output as untrusted data.
- Prefer structured outputs validated against a schema.
- Deterministic post-processing must handle deduplication, normalization, policy checks, and persistence rules.
- Keep prompts versionable when output behavior matters to the product.
- Do not send unnecessary personal data to external AI providers.
- Log operational metadata (provider/model/latency/token usage when available) without logging sensitive prompts or user data by default.
- AI behavior changes require representative evaluation cases when the behavior is product-critical.
- RAG must identify retrieval context separately from generated answers and must not imply unsupported certainty.
- Function/tool calls must have explicit input schemas and authorization checks.
