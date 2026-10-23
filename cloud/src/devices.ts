import { canonicalJson } from './crypto';
import { fail, isPlainObject, nowIso } from './http';
import type { User } from './types';

// The old manual "added to external database" mark is replaced by the computed cloud status.
export const DEPRECATED_FIELDS = ['addedToExternalDatabase', 'addedToExternalDatabaseAt'];
export const MAX_DATA_BYTES = 64 * 1024;
const MAX_ID_LENGTH = 200;

export type Source = 'console' | 'web';
type Action = 'create' | 'update' | 'delete' | 'restore';

export interface DeviceRow {
  id: string;
  data: string;
  revision: number;
  change_id: number;
  created_at: string;
  updated_at: string;
  updated_by: string;
  deleted_at: string | null;
}

export interface Device {
  id: string;
  revision: number;
  deleted: boolean;
  createdAt: string;
  updatedAt: string;
  updatedBy: string;
  data: Record<string, unknown>;
}

export interface HistoryEntry {
  revision: number;
  action: Action;
  changedAt: string;
  changedBy: string;
  source: Source;
  data: Record<string, unknown>;
}

export type WriteResult =
  | { status: 'ok'; device: Device; changed: boolean }
  | { status: 'conflict'; current: Device }
  | { status: 'deleted'; current: Device };

export function toDevice(row: DeviceRow): Device {
  return {
    id: row.id,
    revision: row.revision,
    deleted: row.deleted_at !== null,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    updatedBy: row.updated_by,
    data: JSON.parse(row.data) as Record<string, unknown>,
  };
}

export function validateDeviceId(id: string): string {
  if (id.length === 0 || id.length > MAX_ID_LENGTH || /[\u0000-\u001f\u007f]/.test(id)) {
    fail(400, 'invalid_id', 'Недопустимый идентификатор устройства.');
  }
  return id;
}

// Checks incoming record data and removes deprecated fields.
export function normalizeData(id: string, data: unknown): Record<string, unknown> {
  if (!isPlainObject(data)) {
    fail(400, 'invalid_data', 'Запись устройства должна быть JSON-объектом.');
  }
  const result: Record<string, unknown> = { ...data };
  for (const field of DEPRECATED_FIELDS) {
    delete result[field];
  }
  const computerId = result.computerId;
  if (typeof computerId === 'string' && computerId !== '' && computerId !== id) {
    fail(400, 'id_mismatch', `computerId записи (${computerId}) не совпадает с идентификатором ${id}.`);
  }
  if (new TextEncoder().encode(JSON.stringify(result)).length > MAX_DATA_BYTES) {
    fail(413, 'too_large', 'Запись устройства больше 64 КБ.');
  }
  return result;
}

export async function getDeviceRow(db: D1Database, id: string): Promise<DeviceRow | null> {
  return db.prepare('SELECT * FROM devices WHERE id = ?').bind(id).first<DeviceRow>();
}

export async function listDevices(db: D1Database, includeDeleted: boolean): Promise<Device[]> {
  const sql = includeDeleted
    ? 'SELECT * FROM devices ORDER BY id'
    : 'SELECT * FROM devices WHERE deleted_at IS NULL ORDER BY id';
  const { results } = await db.prepare(sql).all<DeviceRow>();
  return results.map(toDevice);
}

// Devices changed after the given history id, oldest change first.
export async function listChanges(
  db: D1Database,
  since: number,
  limit: number,
): Promise<{ cursor: number; hasMore: boolean; devices: Device[] }> {
  const { results } = await db
    .prepare('SELECT * FROM devices WHERE change_id > ? ORDER BY change_id LIMIT ?')
    .bind(since, limit + 1)
    .all<DeviceRow>();
  const hasMore = results.length > limit;
  const page = results.slice(0, limit);
  const cursor = page.length > 0 ? page[page.length - 1].change_id : since;
  return { cursor, hasMore, devices: page.map(toDevice) };
}

export async function getHistory(db: D1Database, id: string): Promise<HistoryEntry[]> {
  const { results } = await db
    .prepare(
      'SELECT revision, action, data, changed_at, changed_by, source FROM device_history WHERE device_id = ? ORDER BY revision DESC',
    )
    .bind(id)
    .all<{ revision: number; action: Action; data: string; changed_at: string; changed_by: string; source: Source }>();
  return results.map((row) => ({
    revision: row.revision,
    action: row.action,
    changedAt: row.changed_at,
    changedBy: row.changed_by,
    source: row.source,
    data: JSON.parse(row.data) as Record<string, unknown>,
  }));
}

function sameContent(a: Record<string, unknown>, b: Record<string, unknown>): boolean {
  return canonicalJson(a) === canonicalJson(b);
}

// Writes a new revision and its history row in one atomic batch. Returns null when another
// writer took the same revision first; the caller then re-reads the device and decides again.
async function writeRevision(
  db: D1Database,
  id: string,
  current: DeviceRow | null,
  data: Record<string, unknown>,
  action: Action,
  user: User,
  source: Source,
): Promise<Device | null> {
  const revision = (current?.revision ?? 0) + 1;
  const now = nowIso();
  const json = JSON.stringify(data);
  const changeId = '(SELECT id FROM device_history WHERE device_id = ?1 AND revision = ?2)';
  const history = db
    .prepare(
      'INSERT INTO device_history (device_id, revision, action, data, changed_at, changed_by, source) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)',
    )
    .bind(id, revision, action, json, now, user.email, source);
  const device = current
    ? db
        .prepare(
          `UPDATE devices SET data = ?3, revision = ?2, change_id = ${changeId}, updated_at = ?4, updated_by = ?5, deleted_at = ?6 WHERE id = ?1 AND revision = ?7`,
        )
        .bind(id, revision, json, now, user.email, action === 'delete' ? now : null, current.revision)
    : db
        .prepare(
          `INSERT INTO devices (id, data, revision, change_id, created_at, updated_at, updated_by, deleted_at) VALUES (?1, ?3, ?2, ${changeId}, ?4, ?4, ?5, NULL)`,
        )
        .bind(id, revision, json, now, user.email);
  try {
    await db.batch([history, device]);
  } catch (error) {
    if (String(error).includes('UNIQUE constraint failed') || String(error).includes('SQLITE_CONSTRAINT')) {
      return null;
    }
    throw error;
  }
  const row = await getDeviceRow(db, id);
  return row ? toDevice(row) : null;
}

const MAX_ATTEMPTS = 3;

async function raceLost(db: D1Database, id: string): Promise<WriteResult> {
  const row = await getDeviceRow(db, id);
  if (!row) throw new Error(`Device ${id} disappeared during a write.`);
  return { status: 'conflict', current: toDevice(row) };
}

// Console upload. baseRevision is the cloud revision the local copy was based on (null for a new record).
export async function putDevice(
  db: D1Database,
  id: string,
  baseRevision: number | null,
  incoming: Record<string, unknown>,
  options: { restore: boolean; user: User; source: Source },
): Promise<WriteResult> {
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
    const row = await getDeviceRow(db, id);
    let action: Action = 'create';
    if (row) {
      const current = toDevice(row);
      if (current.deleted) {
        if (!options.restore) return { status: 'deleted', current };
        action = 'restore';
      } else {
        if (sameContent(current.data, incoming)) return { status: 'ok', device: current, changed: false };
        if (baseRevision !== current.revision) return { status: 'conflict', current };
        action = 'update';
      }
    }
    const device = await writeRevision(db, id, row, incoming, action, options.user, options.source);
    if (device) return { status: 'ok', device, changed: true };
  }
  return raceLost(db, id);
}

// Web edit: applies changed fields on top of the revision the editor was looking at.
export async function patchDevice(
  db: D1Database,
  id: string,
  baseRevision: number,
  changes: Record<string, unknown>,
  user: User,
): Promise<WriteResult> {
  if ('computerId' in changes) {
    fail(400, 'readonly_field', 'Поле computerId изменить нельзя.');
  }
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
    const row = await getDeviceRow(db, id);
    if (!row) fail(404, 'not_found', 'Устройство не найдено.');
    const current = toDevice(row);
    if (current.deleted) return { status: 'deleted', current };
    if (baseRevision !== current.revision) return { status: 'conflict', current };
    const merged = normalizeData(id, { ...current.data, ...changes });
    if (sameContent(current.data, merged)) return { status: 'ok', device: current, changed: false };
    const device = await writeRevision(db, id, row, merged, 'update', user, 'web');
    if (device) return { status: 'ok', device, changed: true };
  }
  return raceLost(db, id);
}

// Soft delete (deleted = true) or restore (deleted = false). Repeating the same call changes nothing.
export async function setDeleted(db: D1Database, id: string, deleted: boolean, user: User): Promise<Device> {
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt++) {
    const row = await getDeviceRow(db, id);
    if (!row) fail(404, 'not_found', 'Устройство не найдено.');
    const current = toDevice(row);
    if (current.deleted === deleted) return current;
    const device = await writeRevision(db, id, row, current.data, deleted ? 'delete' : 'restore', user, 'web');
    if (device) return device;
  }
  fail(409, 'busy', 'Запись одновременно изменяется. Повторите попытку.');
}
