-- Property issued to people that is not a computer (monitors, printers, power stations, flash drives, SIM cards…),
-- from the property register. Computers stay in devices, their responsible person is the device field ownerId.
-- owner_id: the person materially responsible (people.id). Changes are written to audit_log (item.*).
CREATE TABLE items (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  type        TEXT NOT NULL,
  model       TEXT NOT NULL DEFAULT '',
  serial      TEXT NOT NULL DEFAULT '',
  location    TEXT NOT NULL DEFAULT '',
  owner_id    INTEGER REFERENCES people (id),
  note        TEXT NOT NULL DEFAULT '',
  created_at  TEXT NOT NULL,
  created_by  TEXT NOT NULL,
  updated_at  TEXT NOT NULL,
  updated_by  TEXT NOT NULL,
  deleted_at  TEXT
);

CREATE INDEX items_owner ON items (owner_id);
