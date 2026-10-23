import type { Context } from 'hono';
import { HTTPException } from 'hono/http-exception';
import type { ContentfulStatusCode } from 'hono/utils/http-status';
import { hasRole, type AppEnv, type Role } from './types';

// Error body shape shared by all endpoints: { error: { code, message }, ...extra }.
// Messages are in Russian because the console shows them to the user as is.
export function fail(
  status: ContentfulStatusCode,
  code: string,
  message: string,
  extra: Record<string, unknown> = {},
): never {
  throw new HTTPException(status, {
    res: Response.json({ error: { code, message }, ...extra }, { status }),
  });
}

export function requireRole(c: Context<AppEnv>, minimum: Role): void {
  if (!hasRole(c.get('user'), minimum)) {
    fail(403, 'forbidden', 'Недостаточно прав для этого действия.');
  }
}

export async function readJsonObject(c: Context<AppEnv>): Promise<Record<string, unknown>> {
  let body: unknown;
  try {
    body = await c.req.json();
  } catch {
    fail(400, 'invalid_json', 'Тело запроса должно быть JSON-объектом.');
  }
  if (!isPlainObject(body)) {
    fail(400, 'invalid_json', 'Тело запроса должно быть JSON-объектом.');
  }
  return body;
}

export function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function nowIso(): string {
  return new Date().toISOString();
}
