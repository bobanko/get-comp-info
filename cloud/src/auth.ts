import type { Context } from 'hono';
import { getCookie } from 'hono/cookie';
import { createMiddleware } from 'hono/factory';
import { createLocalJWKSet, createRemoteJWKSet, jwtVerify, type JSONWebKeySet, type JWTVerifyGetKey } from 'jose';
import { sha256Hex } from './crypto';
import { fail, nowIso } from './http';
import { hasRole, toUser, type AppEnv, type UserRow } from './types';

// Console requests: "Authorization: Bearer <personal key>".
export const consoleAuth = createMiddleware<AppEnv>(async (c, next) => {
  const match = /^Bearer\s+(\S+)$/i.exec(c.req.header('Authorization') ?? '');
  if (!match) {
    fail(401, 'unauthorized', 'Нужен личный ключ доступа.');
  }
  const keyHash = await sha256Hex(match[1]);
  const row = await c.env.DB.prepare(
    `SELECT k.id AS key_id, k.label AS key_label, u.*
       FROM api_keys k JOIN users u ON u.email = k.user_email
      WHERE k.key_hash = ? AND k.revoked_at IS NULL`,
  )
    .bind(keyHash)
    .first<UserRow & { key_id: string; key_label: string }>();
  if (!row) {
    fail(401, 'invalid_key', 'Ключ неверный или отозван.');
  }
  const user = toUser(row);
  if (!user.active) {
    fail(403, 'user_disabled', 'Пользователь отключён.');
  }
  if (!hasRole(user, 'harvester')) {
    fail(403, 'forbidden', 'Роль пользователя не даёт доступа из консоли.');
  }
  c.set('user', user);
  c.set('apiKey', { id: row.key_id, label: row.key_label });
  c.executionCtx.waitUntil(
    c.env.DB.prepare('UPDATE api_keys SET last_used_at = ? WHERE id = ?').bind(nowIso(), row.key_id).run(),
  );
  await next();
});

// Pending requests kept at most, so strangers cannot flood the admin's list.
export const MAX_PENDING_REQUESTS = 100;

// Web requests: the user signed in through Cloudflare Access, the role comes from the users table.
// Access lets any email in, so this is the real gate: strangers only leave an access request.
export const webAuth = createMiddleware<AppEnv>(async (c, next) => {
  const email = await resolveAccessEmail(c);
  const row = await c.env.DB.prepare('SELECT * FROM users WHERE email = ?').bind(email).first<UserRow>();
  if (!row) {
    return requestAccess(c.env.DB, email);
  }
  if (row.active !== 1) {
    fail(403, 'user_disabled', `Доступ для ${email} отключён администратором.`);
  }
  c.set('user', toUser(row));
  await next();
});

// Records (or refreshes) the request of a signed-in stranger, then always refuses.
async function requestAccess(db: D1Database, email: string): Promise<never> {
  const now = nowIso();
  const existing = await db.prepare('SELECT rejected_at FROM access_requests WHERE email = ?').bind(email).first<{ rejected_at: string | null }>();
  if (existing?.rejected_at) {
    fail(403, 'access_denied', `Администратор отклонил запрос на доступ для ${email}.`);
  }
  if (existing) {
    await db.prepare('UPDATE access_requests SET last_seen_at = ? WHERE email = ?').bind(now, email).run();
  } else {
    const pending = await db.prepare('SELECT COUNT(*) AS count FROM access_requests WHERE rejected_at IS NULL').first<{ count: number }>();
    if ((pending?.count ?? 0) >= MAX_PENDING_REQUESTS) {
      fail(403, 'access_requests_full', `Нет доступа для ${email}. Запросов на доступ слишком много: попросите администратора добавить вас вручную.`);
    }
    await db
      .prepare('INSERT INTO access_requests (email, requested_at, last_seen_at) VALUES (?, ?, ?) ON CONFLICT (email) DO NOTHING')
      .bind(email, now, now)
      .run();
  }
  fail(403, 'access_pending', `Запрос на доступ для ${email} отправлен администратору. Когда он подтвердит, обновите страницу.`);
}

let remoteJwks: { domain: string; keys: JWTVerifyGetKey } | null = null;

function teamDomain(env: Env): string {
  return env.ACCESS_TEAM_DOMAIN.replace(/^https?:\/\//, '').replace(/\/+$/, '');
}

function accessKeys(env: Env): JWTVerifyGetKey {
  // ACCESS_JWKS lets tests verify tokens without fetching Cloudflare's public keys.
  if (env.ACCESS_JWKS) {
    return createLocalJWKSet(JSON.parse(env.ACCESS_JWKS) as JSONWebKeySet);
  }
  const domain = teamDomain(env);
  if (remoteJwks?.domain !== domain) {
    remoteJwks = { domain, keys: createRemoteJWKSet(new URL(`https://${domain}/cdn-cgi/access/certs`)) };
  }
  return remoteJwks.keys;
}

async function resolveAccessEmail(c: Context<AppEnv>): Promise<string> {
  const env = c.env;
  // Local `wrangler dev` only: requests to the real domain never use the development identity.
  const hostname = new URL(c.req.url).hostname;
  if (env.DEV_AUTH_EMAIL && (hostname === 'localhost' || hostname === '127.0.0.1')) {
    return env.DEV_AUTH_EMAIL.toLowerCase();
  }
  if (!env.ACCESS_AUD) {
    fail(500, 'access_not_configured', 'Cloudflare Access не настроен (ACCESS_AUD).');
  }
  if (!env.ACCESS_TEAM_DOMAIN) {
    fail(500, 'access_not_configured', 'Cloudflare Access не настроен (ACCESS_TEAM_DOMAIN).');
  }
  const token = c.req.header('Cf-Access-Jwt-Assertion') ?? getCookie(c, 'CF_Authorization');
  if (!token) {
    fail(401, 'unauthorized', 'Требуется вход через Cloudflare Access.');
  }
  let email: unknown;
  try {
    const { payload } = await jwtVerify(token, accessKeys(env), {
      issuer: `https://${teamDomain(env)}`,
      audience: env.ACCESS_AUD,
    });
    email = payload.email;
  } catch {
    fail(401, 'invalid_token', 'Сессия Cloudflare Access недействительна. Войдите заново.');
  }
  if (typeof email !== 'string' || email === '') {
    fail(401, 'invalid_token', 'В токене Cloudflare Access нет адреса почты.');
  }
  return email.toLowerCase();
}
