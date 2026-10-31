# Security Rules

- Never commit secrets, API keys, private keys, tokens, credentials, or real user data.
- Treat uploaded files and AI-generated content as untrusted input.
- Validate file type, size, and content assumptions at upload boundaries.
- Enforce authorization on the server even when the UI hides an action.
- Do not rely on client-side validation for security decisions.
- Rate-limit authentication, AI generation, uploads, and other abuse-prone endpoints where appropriate.
- Do not log passwords, session tokens, provider credentials, raw private conversations, or unnecessary personal data.
- Prefer deny-by-default access for new privileged operations.
