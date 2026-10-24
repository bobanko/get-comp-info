-- Cloudflare Access sends a sign-in code to any email; the Worker lets in only users.
-- Someone who signed in but is not a user yet leaves a request here for an admin,
-- who approves it (adds the user) or rejects it.
CREATE TABLE access_requests (
  email         TEXT PRIMARY KEY,
  requested_at  TEXT NOT NULL,
  last_seen_at  TEXT NOT NULL,
  rejected_at   TEXT,
  rejected_by   TEXT
);
