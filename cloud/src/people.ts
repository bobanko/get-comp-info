// People devices are linked to by number (migrations/0007_people.sql): device fields userId — who actually works
// at the computer — and ownerId — the person materially responsible for it by the issue register.
import { fail, nowIso } from './http';
import type { User } from './types';

export const PERSON_FIELDS = ['userId', 'ownerId'] as const;
export const MAX_PERSON_NAME = 200;
export const MAX_PERSON_NOTE = 500;

export interface Person {
  id: number;
  name: string;
  note: string;
  createdAt: string;
  createdBy: string;
  updatedAt: string;
  updatedBy: string;
  deleted: boolean;
}

interface PersonRow {
  id: number;
  name: string;
  note: string;
  created_at: string;
  created_by: string;
  updated_at: string;
  updated_by: string;
  deleted_at: string | null;
}

function toPerson(row: PersonRow): Person {
  return {
    id: row.id,
    name: row.name,
    note: row.note,
    createdAt: row.created_at,
    createdBy: row.created_by,
    updatedAt: row.updated_at,
    updatedBy: row.updated_by,
    deleted: row.deleted_at !== null,
  };
}

// A device field that links a person: null or a person number.
export function validPersonRef(value: unknown): boolean {
  return value === null || (Number.isInteger(value) && (value as number) >= 1);
}

export function parsePersonId(text: string): number {
  if (!/^[1-9]\d{0,9}$/.test(text)) fail(400, 'invalid_id', 'A person number is required.');
  return Number(text);
}

// Name and note of a new or changed person; only the given fields when partial.
export function validatePerson(body: Record<string, unknown>, partial: boolean): { name?: string; note?: string } {
  const result: { name?: string; note?: string } = {};
  if (!partial || 'name' in body) {
    const name = typeof body.name === 'string' ? body.name.trim().replace(/\s+/g, ' ') : '';
    if (name === '' || name.length > MAX_PERSON_NAME) fail(400, 'invalid_person', `A name up to ${MAX_PERSON_NAME} characters is required.`);
    result.name = name;
  }
  if (!partial || 'note' in body) {
    if (body.note !== undefined && body.note !== null && typeof body.note !== 'string') fail(400, 'invalid_person', 'The note must be text.');
    const note = typeof body.note === 'string' ? body.note.trim() : '';
    if (note.length > MAX_PERSON_NOTE) fail(400, 'invalid_person', `The note is up to ${MAX_PERSON_NOTE} characters.`);
    result.note = note;
  }
  return result;
}

export async function listPeople(db: D1Database, includeDeleted: boolean): Promise<Person[]> {
  const sql = includeDeleted ? 'SELECT * FROM people ORDER BY id' : 'SELECT * FROM people WHERE deleted_at IS NULL ORDER BY id';
  const { results } = await db.prepare(sql).all<PersonRow>();
  return results.map(toPerson);
}

export async function getPerson(db: D1Database, id: number): Promise<Person | null> {
  const row = await db.prepare('SELECT * FROM people WHERE id = ?').bind(id).first<PersonRow>();
  return row ? toPerson(row) : null;
}

export async function createPerson(db: D1Database, value: { name: string; note: string }, user: User): Promise<Person> {
  const now = nowIso();
  const row = await db
    .prepare('INSERT INTO people (name, note, created_at, created_by, updated_at, updated_by) VALUES (?1, ?2, ?3, ?4, ?3, ?4) RETURNING *')
    .bind(value.name, value.note, now, user.email)
    .first<PersonRow>();
  return toPerson(row as PersonRow);
}

export async function updatePerson(db: D1Database, id: number, value: { name?: string; note?: string }, user: User): Promise<{ before: Person; after: Person }> {
  const before = await getPerson(db, id);
  if (!before || before.deleted) fail(404, 'not_found', 'Person not found.');
  const name = value.name ?? before.name;
  const note = value.note ?? before.note;
  const row = await db
    .prepare('UPDATE people SET name = ?1, note = ?2, updated_at = ?3, updated_by = ?4 WHERE id = ?5 RETURNING *')
    .bind(name, note, nowIso(), user.email, id)
    .first<PersonRow>();
  return { before, after: toPerson(row as PersonRow) };
}

// Live devices that link this person as the user or the owner.
export async function devicesOfPerson(db: D1Database, id: number): Promise<{ id: string; hostname: string | null }[]> {
  const { results } = await db
    .prepare(
      `SELECT id, json_extract(data, '$.hostname') AS hostname FROM devices
        WHERE deleted_at IS NULL AND (json_extract(data, '$.userId') = ?1 OR json_extract(data, '$.ownerId') = ?1) ORDER BY id`,
    )
    .bind(id)
    .all<{ id: string; hostname: string | null }>();
  return results;
}

// Soft delete; a person still linked to a device is not deleted (unlink first).
export async function deletePerson(db: D1Database, id: number, user: User): Promise<Person> {
  const person = await getPerson(db, id);
  if (!person || person.deleted) fail(404, 'not_found', 'Person not found.');
  const linked = await devicesOfPerson(db, id);
  if (linked.length > 0) {
    fail(409, 'person_in_use', `The person is linked to ${linked.length} device(s): ${linked.map((d) => d.hostname || d.id).join(', ')}.`);
  }
  const items = await db.prepare('SELECT count(*) AS n FROM items WHERE deleted_at IS NULL AND owner_id = ?').bind(id).first<{ n: number }>();
  if ((items?.n ?? 0) > 0) fail(409, 'person_in_use', `The person is responsible for ${items?.n} item(s) of property.`);
  const now = nowIso();
  const row = await db
    .prepare('UPDATE people SET deleted_at = ?1, updated_at = ?1, updated_by = ?2 WHERE id = ?3 RETURNING *')
    .bind(now, user.email, id)
    .first<PersonRow>();
  return toPerson(row as PersonRow);
}

// The people a web change of a device links must exist and not be deleted.
export async function checkPersonRefs(db: D1Database, changes: Record<string, unknown>): Promise<void> {
  for (const field of PERSON_FIELDS) {
    const value = changes[field];
    if (value === undefined || value === null || !Number.isInteger(value)) continue;
    const person = await getPerson(db, value as number);
    if (!person || person.deleted) fail(400, 'unknown_person', `Person #${String(value)} does not exist.`);
  }
}
