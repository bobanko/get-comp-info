-- Journal of the actions that leave no other trace: personal keys, users, access requests and which program
-- version is current. Device changes, network settings and published releases have their own tables; the
-- «Журнал» page (GET /api/web/audit) shows all of them in one list.
CREATE TABLE audit_log (
  id       INTEGER PRIMARY KEY AUTOINCREMENT,
  at       TEXT NOT NULL,
  actor    TEXT NOT NULL,
  action   TEXT NOT NULL,
  target   TEXT,
  details  TEXT
);

CREATE INDEX audit_log_at ON audit_log (at);
CREATE INDEX device_history_changed_at ON device_history (changed_at);
