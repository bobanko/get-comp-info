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

// Web requests: the user signed in through Cloudflare Access, the role comes from the users table.
export const webAuth = createMiddleware<AppEnv>(async (c, next) => {
  const email = await resolveAccessEmail(c);
  const row = await c.env.DB.prepare('SELECT * FROM users WHERE email = ?').bind(email).first<UserRow>();
  if (!row || row.active !== 1) {
    fail(403, 'not_registered', `Нет доступа для ${email}: пользователь не зарегистрирован или отключён.`);
  }
  c.set('user', toUser(row));
  await next();
});

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
