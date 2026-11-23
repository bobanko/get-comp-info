-- People devices are linked to by number: the actual user of a computer (device field userId) and the person
-- materially responsible for it by the issue register (ownerId). The id is the number people see (#12) and never
-- changes or gets reused, so device records keep only it. Changes are written to audit_log (person.*).
CREATE TABLE people (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  name        TEXT NOT NULL,
  note        TEXT NOT NULL DEFAULT '',
  created_at  TEXT NOT NULL,
  created_by  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  updated_by  TEXT NOT NULL,
  deleted_at  TEXT
);

CREATE INDEX people_name ON people (name);
