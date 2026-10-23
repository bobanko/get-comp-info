import { env } from 'cloudflare:test';
import { exports } from 'cloudflare:workers';
import { importJWK, SignJWT, type JWK } from 'jose';
import { sha256Hex } from '../src/crypto';
import type { Role } from '../src/types';

const BASE_URL = 'https://comp-info.test';

// Unique suffix so tests never depend on each other's data.
export function unique(prefix: string): string {
  return `${prefix}-${crypto.randomUUID()}`;
}

export async function addUser(role: Role, options: { active?: boolean; email?: string } = {}): Promise<string> {
  const email = options.email ?? `${unique(role)}@example.com`;
  await env.DB.prepare('INSERT INTO users (email, name, role, active, created_at) VALUES (?, ?, ?, ?, ?)')
    .bind(email, `Test ${role}`, role, options.active === false ? 0 : 1, new Date().toISOString())
    .run();
  return email;
}

// Inserts a personal key directly, bypassing the role check of POST /keys.
export async function addKey(email: string, label = 'Test key'): Promise<string> {
  const key = `ci_${crypto.randomUUID()}`;
  await env.DB.prepare('INSERT INTO api_keys (id, user_email, label, key_hash, created_at) VALUES (?, ?, ?, ?, ?)')
    .bind(crypto.randomUUID(), email, label, await sha256Hex(key), new Date().toISOString())
    .run();
  return key;
}

export async function accessToken(
  email: string,
  claims: { audience?: string; issuer?: string; privateJwk?: JWK } = {},
): Promise<string> {
  const jwk = claims.privateJwk ?? (JSON.parse(env.TEST_ACCESS_PRIVATE_JWK) as JWK);
  const key = await importJWK(jwk, 'RS256');
  return new SignJWT({ email })
    .setProtectedHeader({ alg: 'RS256', kid: jwk.kid ?? 'test' })
    .setIssuer(claims.issuer ?? 'https://test-team.cloudflareaccess.com')
    .setAudience(claims.audience ?? 'test-aud')
    .setIssuedAt()
    .setExpirationTime('1h')
    .sign(key);
}

export interface ApiResponse {
  status: number;
  body: any;
}

async function send(path: string, method: string, headers: Record<string, string>, body?: unknown): Promise<ApiResponse> {
  const init: RequestInit = { method, headers: { ...headers } };
  if (body !== undefined) {
    init.body = typeof body === 'string' ? body : JSON.stringify(body);
    (init.headers as Record<string, string>)['Content-Type'] = 'application/json';
  }
  const response = await exports.default.fetch(new Request(BASE_URL + path, init));
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

export function consoleRequest(key: string | null, method: string, path: string, body?: unknown) {
  return send('/api/client' + path, method, key ? { Authorization: `Bearer ${key}` } : {}, body);
}

export async function webRequest(email: string | null, method: string, path: string, body?: unknown) {
  const headers: Record<string, string> = email ? { 'Cf-Access-Jwt-Assertion': await accessToken(email) } : {};
  return send('/api/web' + path, method, headers, body);
}

export function webRequestWithToken(token: string, method: string, path: string) {
  return send('/api/web' + path, method, { 'Cf-Access-Jwt-Assertion': token });
}

export function deviceData(id: string, extra: Record<string, unknown> = {}) {
  return {
    computerId: id,
    computerIdSource: 'smbiosUuid',
    hostname: 'PC-' + id.slice(-6),
    ip: '10.0.0.1',
    serial: 'SN' + id.slice(-6),
    comment: '',
    collectedAt: '2026-10-23T10:00:00.0000000+03:00',
    ...extra,
  };
}

export async function harvesterKey(): Promise<{ email: string; key: string }> {
  const email = await addUser('harvester');
  return { email, key: await addKey(email) };
}
