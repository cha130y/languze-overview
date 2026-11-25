# ADR-0007: Traffic data and closed-account records

- **Status:** Accepted (2026-11-07). Both are built: the closed-account records first, then the event log.
- **Requirements:** FR-007, FR-110, FR-111; legal review, findings C1–C3; launch checklist, section 7

## Context

Thailand's Computer Crime Act, section 26, and its 2564 notification require a service provider to keep traffic data for at least 90 days, and the data that identifies a user until at least 90 days after their use of the service ends. The records must keep their integrity, and administrators must not be able to alter them (clause 9). A digital service provider keeps sign-in logs, the connection's date and time, the user ID with the email or social account, the client's IP address, and records of use.

LanguZe kept none of it. Application logs hold no IP address or user ID, Railway keeps its HTTP logs for 7 days, and deleting an account removes everything at once (FR-007). A provider that does not comply faces a fine of up to 500,000 baht. PDPA allows the retention: complying with the law is a lawful basis (s.24(6)), and data kept to comply is exempt from erasure (s.37(3)).

Whether LanguZe is a digital service provider in the notification's sense, and whether this is enough, is question 4 for a Thai lawyer. This ADR builds what the text asks for, so the public opening does not wait on the answer. An answer that asks for more or less changes the list of events, not the design.

## Decision

1. **PostgreSQL, not an external log service.** The records live in LanguZe's own database. No new provider has to join the record of processing, and the volume is small: about 150 bytes a row, roughly 20 MB for 100 daily learners over 90 days, held steady by the deletion.
2. **Closed-account records (FR-111).** `closed_accounts` keeps an account's ID, email address, linked provider accounts, creation and deletion times, and whether an admin deleted it. It is written inside `AccountDeletion`, the one place both the learner's and the admin's deletion go through, in the same transaction, so a deletion without its record cannot happen. A placeholder address (D1) is kept as no address; the provider account it stands for is kept instead.
3. **An event log (FR-110).** `traffic_events` records each sign-up, sign-in, failed sign-in, sign-out, photo upload, tutor message and account deletion, with the account when known, the sign-in method, the client's IP address and the time. The address is Cloudflare's `CF-Connecting-IP`, believed only with the origin secret: Railway's edge replaces `X-Forwarded-For` with Cloudflare's address, which the first events recorded before this was found. A failed sign-in names the account only when the address belongs to one, never the address typed or the password. Not every request is logged: that would grow the database for nothing the law asks. No user agent is kept, since the law does not ask for one.
4. **Unchangeable for 90 days.** Triggers on both tables refuse every UPDATE and TRUNCATE, and any DELETE of a row younger than 90 days by the database's clock, as `admin_actions` refuses every change (D4). Neither has a foreign key, so both outlive the account. The limit: the database's owner could still drop a trigger. If the lawyer's answer requires storage no administrator can alter at all, the records move to a write-once service.
5. **Written with the action.** An upload, a tutor message and a deletion write their event in their own transaction; sign-in, sign-out and a failed sign-in write theirs on their own. If the event cannot be written, the request fails, so no action goes unrecorded.
6. **Deleted after 90 days by a job** that reads the database only when a record is due. It learns the earliest when the API starts and from each new record, so Neon's compute is not woken for nothing (production deployment, section 10).
7. **No endpoint reads them.** An official order (s.18, within 7 to 15 days) is answered by the operator with the queries in production deployment, section 21, which also records who coordinates with officials (C3).
8. **The Privacy Policy says so:** what is kept, for how long, and why.

## Consequences

- FR-007 has an exception: an account's identifying data outlives it by 90 days.
- Each deletion, sign-in, upload and tutor message writes one more row. Each is a request that writes to the database already, so no compute is woken for it.
- The records hold personal data under PDPA. They are listed in the record of processing with their retention, and a learner's access request covers them.
- Admin audit entries after a deletion are a separate question (legal review, question 6).
