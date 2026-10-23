-- Users and roles. Emails are stored in lower case.
CREATE TABLE users (
  email       TEXT PRIMARY KEY,
  name        TEXT NOT NULL,
  role        TEXT NOT NULL CHECK (role IN ('viewer', 'harvester', 'editor', 'admin')),
  active      INTEGER NOT NULL DEFAULT 1 CHECK (active IN (0, 1)),
  created_at  TEXT NOT NULL
);

-- Personal console keys. Only the SHA-256 of a key is stored.
CREATE TABLE api_keys (
  id            TEXT PRIMARY KEY,
  user_email    TEXT NOT NULL REFERENCES users (email),
  label         TEXT NOT NULL,
  key_hash      TEXT NOT NULL UNIQUE,
  created_at    TEXT NOT NULL,
  last_used_at  TEXT,
  revoked_at    TEXT
);

CREATE INDEX api_keys_user ON api_keys (user_email);

-- Current state of every device. change_id points at the latest history row
-- and lets consoles download only what changed since their last sync.
CREATE TABLE devices (
  id          TEXT PRIMARY KEY,
  data        TEXT NOT NULL,
  revision    INTEGER NOT NULL,
  change_id   INTEGER NOT NULL,
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  updated_by  TEXT NOT NULL,
  deleted_at  TEXT
);

CREATE INDEX devices_change ON devices (change_id);

-- Every version of every device. The unique (device_id, revision) pair makes
-- concurrent writes of the same revision fail instead of overwriting each other.
CREATE TABLE device_history (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id   TEXT NOT NULL,
  revision    INTEGER NOT NULL,
  action      TEXT NOT NULL CHECK (action IN ('create', 'update', 'delete', 'restore')),
  data        TEXT NOT NULL,
  changed_at  TEXT NOT NULL,
  changed_by  TEXT NOT NULL,
  source      TEXT NOT NULL CHECK (source IN ('console', 'web')),
  UNIQUE (device_id, revision)
);

-- Published versions of the console program (see PROGRAM_UPDATE.md).
CREATE TABLE program_releases (
  version       TEXT PRIMARY KEY,
  commit_sha    TEXT NOT NULL,
  manifest      TEXT NOT NULL,
  package       BLOB NOT NULL,
  sha256        TEXT NOT NULL,
  size          INTEGER NOT NULL,
  published_at  TEXT NOT NULL,
  is_current    INTEGER NOT NULL DEFAULT 0 CHECK (is_current IN (0, 1))
);

CREATE UNIQUE INDEX program_releases_single_current ON program_releases (is_current) WHERE is_current = 1;
