// Property that is not a computer (migrations/0008_items.sql): type, model, serial number, place, the materially
// responsible person (ownerId, a people number) and a note. Computers are device records with the field ownerId.
import { fail, nowIso } from './http';
import { getPerson } from './people';
import type { User } from './types';

const LIMITS = { type: 100, model: 200, serial: 200, location: 200, note: 1000 } as const;
type TextField = keyof typeof LIMITS;
const TEXT_FIELDS = Object.keys(LIMITS) as TextField[];

export interface Item {
  id: number;
  type: string;
  model: string;
  serial: string;
  location: string;
  ownerId: number | null;
  note: string;
  createdAt: string;
  createdBy: string;
  updatedAt: string;
  updatedBy: string;
  deleted: boolean;
}

export type ItemValue = Partial<Record<TextField, string>> & { ownerId?: number | null };

interface ItemRow {
  id: number;
  type: string;
  model: string;
  serial: string;
  location: string;
  owner_id: number | null;
  note: string;
  created_at: string;
  created_by: string;
  updated_at: string;
  updated_by: string;
  deleted_at: string | null;
}

function toItem(row: ItemRow): Item {
  return {
    id: row.id,
    type: row.type,
    model: row.model,
    serial: row.serial,
    location: row.location,
    ownerId: row.owner_id,
    note: row.note,
    createdAt: row.created_at,
    createdBy: row.created_by,
    updatedAt: row.updated_at,
    updatedBy: row.updated_by,
    deleted: row.deleted_at !== null,
  };
}

export function parseItemId(text: string): number {
  if (!/^[1-9]\d{0,9}$/.test(text)) fail(400, 'invalid_id', 'An item number is required.');
  return Number(text);
}

// The fields of a new or changed item; only the given ones when partial. The person must exist and not be deleted.
export async function validateItem(db: D1Database, body: Record<string, unknown>, partial: boolean): Promise<ItemValue> {
  const result: ItemValue = {};
  for (const field of TEXT_FIELDS) {
    if (partial && !(field in body)) continue;
    const raw = body[field];
    if (raw !== undefined && raw !== null && typeof raw !== 'string') fail(400, 'invalid_item', `${field} must be text.`);
    const value = typeof raw === 'string' ? raw.trim().replace(/[ \t]+/g, ' ') : '';
    if (value.length > LIMITS[field]) fail(400, 'invalid_item', `${field} is up to ${LIMITS[field]} characters.`);
    result[field] = value;
  }
  if (result.type === '') fail(400, 'invalid_item', 'The type is required.');
  if (!partial || 'ownerId' in body) {
    const owner = body.ownerId ?? null;
    if (owner !== null) {
      if (!Number.isInteger(owner) || (owner as number) < 1) fail(400, 'invalid_person_ref', 'ownerId must be a person number or null.');
      const person = await getPerson(db, owner as number);
      if (!person || person.deleted) fail(400, 'unknown_person', `Person #${String(owner)} does not exist.`);
    }
    result.ownerId = owner as number | null;
  }
  return result;
}

export async function listItems(db: D1Database, includeDeleted: boolean): Promise<Item[]> {
  const sql = includeDeleted ? 'SELECT * FROM items ORDER BY id' : 'SELECT * FROM items WHERE deleted_at IS NULL ORDER BY id';
  const { results } = await db.prepare(sql).all<ItemRow>();
  return results.map(toItem);
}

export async function getItem(db: D1Database, id: number): Promise<Item | null> {
  const row = await db.prepare('SELECT * FROM items WHERE id = ?').bind(id).first<ItemRow>();
  return row ? toItem(row) : null;
}

export async function createItem(db: D1Database, value: ItemValue, user: User): Promise<Item> {
  const now = nowIso();
  const row = await db
    .prepare(
      `INSERT INTO items (type, model, serial, location, owner_id, note, created_at, created_by, updated_at, updated_by)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?7, ?8) RETURNING *`,
    )
    .bind(value.type ?? '', value.model ?? '', value.serial ?? '', value.location ?? '', value.ownerId ?? null, value.note ?? '', now, user.email)
    .first<ItemRow>();
  return toItem(row as ItemRow);
}

export async function updateItem(db: D1Database, id: number, value: ItemValue, user: User): Promise<{ before: Item; after: Item }> {
  const before = await getItem(db, id);
  if (!before || before.deleted) fail(404, 'not_found', 'Item not found.');
  const next = { ...before, ...value };
  const row = await db
    .prepare(
      `UPDATE items SET type = ?1, model = ?2, serial = ?3, location = ?4, owner_id = ?5, note = ?6, updated_at = ?7, updated_by = ?8
        WHERE id = ?9 RETURNING *`,
    )
    .bind(next.type, next.model, next.serial, next.location, next.ownerId ?? null, next.note, nowIso(), user.email, id)
    .first<ItemRow>();
  return { before, after: toItem(row as ItemRow) };
}

export async function deleteItem(db: D1Database, id: number, user: User): Promise<Item> {
  const item = await getItem(db, id);
  if (!item || item.deleted) fail(404, 'not_found', 'Item not found.');
  const now = nowIso();
  const row = await db
    .prepare('UPDATE items SET deleted_at = ?1, updated_at = ?1, updated_by = ?2 WHERE id = ?3 RETURNING *')
    .bind(now, user.email, id)
    .first<ItemRow>();
  return toItem(row as ItemRow);
}

// What changed, as [before, after], for the journal.
export function itemChanges(before: Item, after: Item): Record<string, [unknown, unknown]> {
  const changes: Record<string, [unknown, unknown]> = {};
  for (const field of [...TEXT_FIELDS, 'ownerId'] as const) {
    if (before[field] !== after[field]) changes[field] = [before[field], after[field]];
  }
  return changes;
}
