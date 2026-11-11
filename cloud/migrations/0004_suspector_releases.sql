-- Public standalone SUSpector releases. They are separate from the internal toolbox package.
CREATE TABLE suspector_releases (
  version       TEXT PRIMARY KEY,
  commit_sha    TEXT NOT NULL,
  manifest      TEXT NOT NULL,
  package       BLOB NOT NULL,
  sha256        TEXT NOT NULL,
  size          INTEGER NOT NULL,
  published_at  TEXT NOT NULL,
  is_current    INTEGER NOT NULL DEFAULT 0 CHECK (is_current IN (0, 1))
);

CREATE UNIQUE INDEX suspector_releases_single_current ON suspector_releases (is_current) WHERE is_current = 1;
