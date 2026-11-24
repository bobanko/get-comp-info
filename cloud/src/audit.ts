import type { Context } from 'hono';
import { fail, nowIso } from './http';
import type { AppEnv } from './types';

// Actions written to audit_log. The rest of the journal comes from device_history, settings_history and the
// release tables, which already record who changed what and when.
export type AuditAction =
  | 'key.create'
  | 'key.revoke'
  | 'key.rename'
  | 'user.create'
  | 'user.update'
  | 'access.reject'
  | 'person.create'
  | 'person.update'
  | 'person.delete'
  | 'item.create'
  | 'item.update'
  | 'item.delete'
  | 'release.current'
  | 'suspector-release.current';

// Records an action of the signed-in user after it is done. A failed write is logged and does not undo or fail
// the action: the journal must never stop people from working.
export async function audit(
  c: Context<AppEnv>,
  action: AuditAction,
  target: string | null,
  details: Record<string, unknown> | null = null,
): Promise<void> {
  try {
    await c.env.DB.prepare('INSERT INTO audit_log (at, actor, action, target, details) VALUES (?, ?, ?, ?, ?)')
      .bind(nowIso(), c.get('user').email, action, target, details === null ? null : JSON.stringify(details))
      .run();
  } catch (error) {
    console.error('audit_log write failed', action, target, error);
  }
}

export type JournalCategory = 'devices' | 'access' | 'programs';

export interface JournalEntry {
  at: string;
  kind: 'audit' | 'device' | 'settings' | 'release' | 'suspector-release';
  category: JournalCategory;
  action: string;
  actor: string;
  actorName: string | null;
  actorKey: string | null;
  source: string;
  target: string | null;
  details: Record<string, unknown> | null;
}

// Every kind of entry as the same columns. seq is the rowid inside its table: with at and kind it orders entries
// of the same moment (one console sync writes many) and makes the cursor of the next page exact.
const JOURNAL = `
  SELECT a.at AS at, 'audit' AS kind, a.id AS seq,
         CASE WHEN a.action LIKE '%release.current' THEN 'programs' WHEN a.action LIKE 'person.%' OR a.action LIKE 'item.%' THEN 'devices' ELSE 'access' END AS category,
         a.action AS action, a.actor AS actor, u.name AS actor_name, NULL AS actor_key, 'web' AS source,
         a.target AS target, a.details AS details
    FROM audit_log a LEFT JOIN users u ON u.email = a.actor
  UNION ALL
  SELECT h.changed_at, 'device', h.id, 'devices', h.action, h.changed_by, u.name, k.label, h.source, h.device_id,
         json_object('revision', h.revision, 'hostname', json_extract(h.data, '$.hostname'))
    FROM device_history h LEFT JOIN users u ON u.email = h.changed_by LEFT JOIN api_keys k ON k.id = h.key_id
  UNION ALL
  SELECT s.changed_at, 'settings', s.rowid, 'programs', 'update', s.changed_by, u.name, NULL, 'web', s.key,
         json_object('revision', s.revision)
    FROM settings_history s LEFT JOIN users u ON u.email = s.changed_by
  UNION ALL
  SELECT r.published_at, 'release', r.rowid, 'programs', 'publish', 'GitHub Actions', NULL, NULL, 'github', r.version,
         json_object('commit', r.commit_sha)
    FROM program_releases r
  UNION ALL
  SELECT r.published_at, 'suspector-release', r.rowid, 'programs', 'publish', 'GitHub Actions', NULL, NULL, 'github', r.version,
         json_object('commit', r.commit_sha)
    FROM suspector_releases r`;

interface JournalRow {
  at: string;
  kind: JournalEntry['kind'];
  seq: number;
  category: JournalCategory;
  action: string;
  actor: string;
  actor_name: string | null;
  actor_key: string | null;
  source: string;
  target: string | null;
  details: string | null;
}

const CATEGORIES: readonly string[] = ['devices', 'access', 'programs'];
export const JOURNAL_PAGE = 100;

// The cursor of the next page: the position of the last entry, as at|kind|seq in base64url.
function encodeCursor(row: JournalRow): string {
  return btoa(`${row.at}|${row.kind}|${row.seq}`).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function decodeCursor(cursor: string): { at: string; kind: string; seq: number } {
  try {
    const [at, kind, seq] = atob(cursor.replace(/-/g, '+').replace(/_/g, '/')).split('|');
    if (at && kind && /^\d+$/.test(seq)) return { at, kind, seq: Number(seq) };
  } catch {
    // falls through to the error below
  }
  fail(400, 'invalid_cursor', 'The page cursor is not valid.');
}

// Newest first, one page at a time; category limits it to devices, access (keys, users, requests) or programs
// (releases, network settings).
export async function readJournal(db: D1Database, options: { category?: string; cursor?: string }) {
  const where: string[] = [];
  const values: unknown[] = [];
  if (options.category !== undefined && options.category !== '' && options.category !== 'all') {
    if (!CATEGORIES.includes(options.category)) fail(400, 'invalid_category', 'The category must be devices, access or programs.');
    where.push('category = ?');
    values.push(options.category);
  }
  if (options.cursor) {
    const c = decodeCursor(options.cursor);
    where.push('(at < ? OR (at = ? AND (kind < ? OR (kind = ? AND seq < ?))))');
    values.push(c.at, c.at, c.kind, c.kind, c.seq);
  }
  const sql = `SELECT * FROM (${JOURNAL}) ${where.length ? 'WHERE ' + where.join(' AND ') : ''}
               ORDER BY at DESC, kind DESC, seq DESC LIMIT ?`;
  const { results } = await db
    .prepare(sql)
    .bind(...values, JOURNAL_PAGE + 1)
    .all<JournalRow>();
  const more = results.length > JOURNAL_PAGE;
  const rows = more ? results.slice(0, JOURNAL_PAGE) : results;
  return {
    entries: rows.map(
      (row): JournalEntry => ({
        at: row.at,
        kind: row.kind,
        category: row.category,
        action: row.action,
        actor: row.actor,
        actorName: row.actor_name,
        actorKey: row.actor_key,
        source: row.source,
        target: row.target,
        details: row.details === null ? null : (JSON.parse(row.details) as Record<string, unknown>),
      }),
    ),
    next: more ? encodeCursor(rows[rows.length - 1]) : null,
  };
}
