// Program releases: published by GitHub Actions, downloaded by the console, managed by admins.
import { Hono } from 'hono';
import { createMiddleware } from 'hono/factory';
import { createLocalJWKSet, createRemoteJWKSet, jwtVerify, type JSONWebKeySet, type JWTVerifyGetKey } from 'jose';
import { sha256Hex } from './crypto';
import { fail, nowIso } from './http';
import type { AppEnv } from './types';
import { readZip, ZipError } from './zip';

export const MAX_PACKAGE_BYTES = 1024 * 1024;
export const GITHUB_ISSUER = 'https://token.actions.githubusercontent.com';
export const RELEASE_AUDIENCE = 'comp-info-release';
const VERSION_PATTERN = /^\d{4}\.\d{2}\.\d{2}\.\d{1,4}$/;
// Only files from the program root: scripts, launchers and the cloud settings.
const PROGRAM_FILE = /^[^\\/:*?"<>|\u0000-\u001f]+\.(ps1|bat|cmd)$/i;

export interface ManifestFile {
  path: string;
  sha256: string;
  size: number;
}

export interface Manifest {
  version: string;
  commit: string;
  publishedAt?: string;
  notes: string[];
  files: ManifestFile[];
}

interface ReleaseRow {
  version: string;
  commit_sha: string;
  manifest: string;
  sha256: string;
  size: number;
  published_at: string;
  is_current: number;
}

export function isProgramFile(path: string): boolean {
  return path === 'cloud-config.json' || PROGRAM_FILE.test(path);
}

function toRelease(row: ReleaseRow) {
  const manifest = JSON.parse(row.manifest) as Manifest;
  return {
    version: row.version,
    sha256: row.sha256,
    size: row.size,
    publishedAt: row.published_at,
    commit: row.commit_sha,
    notes: manifest.notes ?? [],
    isCurrent: row.is_current === 1,
  };
}

const RELEASE_COLUMNS = 'version, commit_sha, manifest, sha256, size, published_at, is_current';

// Checks the archive against its manifest.json and returns the manifest.
export async function validatePackage(bytes: Uint8Array): Promise<Manifest> {
  if (bytes.byteLength > MAX_PACKAGE_BYTES) fail(413, 'too_large', 'Архив версии больше 1 МБ.');
  let entries;
  try {
    entries = await readZip(bytes);
  } catch (error) {
    if (error instanceof ZipError) fail(400, 'invalid_package', error.message);
    throw error;
  }
  const byName = new Map(entries.map((entry) => [entry.name, entry.data]));
  const manifestData = byName.get('manifest.json');
  if (!manifestData) fail(400, 'invalid_package', 'В архиве нет manifest.json.');

  let manifest: Manifest;
  try {
    manifest = JSON.parse(new TextDecoder().decode(manifestData).replace(/^﻿/, '')) as Manifest;
  } catch {
    fail(400, 'invalid_manifest', 'manifest.json не является JSON.');
  }
  if (typeof manifest.version !== 'string' || !VERSION_PATTERN.test(manifest.version)) {
    fail(400, 'invalid_version', 'Версия должна быть в формате ГГГГ.ММ.ДД.N.');
  }
  if (typeof manifest.commit !== 'string' || manifest.commit === '') fail(400, 'invalid_manifest', 'В manifest.json нет commit.');
  if (manifest.notes !== undefined && !(Array.isArray(manifest.notes) && manifest.notes.every((note) => typeof note === 'string'))) {
    fail(400, 'invalid_manifest', 'notes в manifest.json должен быть списком строк.');
  }
  if (!Array.isArray(manifest.files) || manifest.files.length === 0) fail(400, 'invalid_manifest', 'В manifest.json нет файлов.');

  const listed = new Set<string>();
  for (const file of manifest.files) {
    if (typeof file?.path !== 'string' || !isProgramFile(file.path)) {
      fail(400, 'invalid_manifest', `Недопустимый файл в manifest.json: ${String(file?.path)}`);
    }
    if (listed.has(file.path.toLowerCase())) fail(400, 'invalid_manifest', `Файл указан дважды: ${file.path}`);
    listed.add(file.path.toLowerCase());
    const data = byName.get(file.path);
    if (!data) fail(400, 'invalid_package', `В архиве нет файла ${file.path}.`);
    if (data.byteLength !== file.size || (await sha256Hex(data)) !== String(file.sha256).toLowerCase()) {
      fail(400, 'invalid_package', `Файл ${file.path} не совпадает с manifest.json.`);
    }
  }
  for (const name of byName.keys()) {
    if (name !== 'manifest.json' && !listed.has(name.toLowerCase())) {
      fail(400, 'invalid_package', `Файла ${name} нет в manifest.json.`);
    }
  }
  return { ...manifest, notes: manifest.notes ?? [] };
}

export async function getCurrentRelease(db: D1Database) {
  const row = await db.prepare(`SELECT ${RELEASE_COLUMNS} FROM program_releases WHERE is_current = 1`).first<ReleaseRow>();
  return row ? toRelease(row) : null;
}

export async function listReleases(db: D1Database) {
  const { results } = await db.prepare(`SELECT ${RELEASE_COLUMNS} FROM program_releases ORDER BY published_at DESC`).all<ReleaseRow>();
  return results.map(toRelease);
}

export async function getReleasePackage(db: D1Database, version: string): Promise<{ bytes: Uint8Array; sha256: string } | null> {
  const row = await db
    .prepare('SELECT package, sha256 FROM program_releases WHERE version = ?')
    .bind(version)
    .first<{ package: ArrayBuffer | number[]; sha256: string }>();
  if (!row) return null;
  // D1 returns BLOB columns as an array of bytes.
  return { bytes: new Uint8Array(row.package), sha256: row.sha256 };
}

export async function makeCurrent(db: D1Database, version: string): Promise<boolean> {
  const exists = await db.prepare('SELECT version FROM program_releases WHERE version = ?').bind(version).first();
  if (!exists) return false;
  await db.batch([
    db.prepare('UPDATE program_releases SET is_current = 0 WHERE is_current = 1'),
    db.prepare('UPDATE program_releases SET is_current = 1 WHERE version = ?').bind(version),
  ]);
  return true;
}

// ---------- Publishing from GitHub Actions ----------

let githubKeys: JWTVerifyGetKey | null = null;

function githubJwks(env: Env): JWTVerifyGetKey {
  // GITHUB_OIDC_JWKS lets tests verify tokens without fetching GitHub's public keys.
  if (env.GITHUB_OIDC_JWKS) return createLocalJWKSet(JSON.parse(env.GITHUB_OIDC_JWKS) as JSONWebKeySet);
  githubKeys ??= createRemoteJWKSet(new URL(`${GITHUB_ISSUER}/.well-known/jwks`));
  return githubKeys;
}

// GitHub Actions OIDC token of a tag push in the release repository: no shared secret needed.
export const releaseAuth = createMiddleware<AppEnv>(async (c, next) => {
  const hostname = new URL(c.req.url).hostname;
  if (c.env.DEV_AUTH_EMAIL && (hostname === 'localhost' || hostname === '127.0.0.1')) {
    // Local development: publish without GitHub.
    c.set('releaseTag', null);
    await next();
    return;
  }
  const match = /^Bearer\s+(\S+)$/i.exec(c.req.header('Authorization') ?? '');
  if (!match) fail(401, 'unauthorized', 'Нужен OIDC-токен GitHub Actions.');
  let claims: Record<string, unknown>;
  try {
    ({ payload: claims } = await jwtVerify(match[1], githubJwks(c.env), { issuer: GITHUB_ISSUER, audience: RELEASE_AUDIENCE }));
  } catch {
    fail(401, 'invalid_token', 'OIDC-токен GitHub недействителен.');
  }
  if (String(claims.repository ?? '').toLowerCase() !== c.env.RELEASE_REPOSITORY.toLowerCase()) {
    fail(403, 'forbidden', 'Публиковать версии можно только из репозитория программы.');
  }
  if (claims.ref_type !== 'tag' || typeof claims.ref !== 'string' || !claims.ref.startsWith('refs/tags/')) {
    fail(403, 'forbidden', 'Публиковать версии можно только по тегу.');
  }
  c.set('releaseTag', claims.ref.slice('refs/tags/'.length));
  await next();
});

export const releaseApi = new Hono<AppEnv>();

releaseApi.use('*', releaseAuth);

releaseApi.post('/', async (c) => {
  const bytes = new Uint8Array(await c.req.arrayBuffer());
  return c.json(await storeRelease(c.env.DB, bytes, c.get('releaseTag') ?? null), 201);
});

// Validates and saves a release archive, making it current. With a tag (GitHub Actions)
// the tag must name the archive's version; admins publishing with a personal key pass null.
export async function storeRelease(db: D1Database, bytes: Uint8Array, tag: string | null) {
  const manifest = await validatePackage(bytes);
  if (tag !== null && tag !== `v${manifest.version}`) {
    fail(400, 'tag_mismatch', `Тег ${tag} не совпадает с версией ${manifest.version} в manifest.json.`);
  }
  const existing = await db.prepare('SELECT version FROM program_releases WHERE version = ?').bind(manifest.version).first();
  if (existing) fail(409, 'release_exists', `Версия ${manifest.version} уже опубликована.`);

  const sha256 = await sha256Hex(bytes);
  const publishedAt = nowIso();
  await db.batch([
    db.prepare('UPDATE program_releases SET is_current = 0 WHERE is_current = 1'),
    db
      .prepare(
        `INSERT INTO program_releases (version, commit_sha, manifest, package, sha256, size, published_at, is_current)
         VALUES (?, ?, ?, ?, ?, ?, ?, 1)`,
      )
      .bind(manifest.version, manifest.commit, JSON.stringify(manifest), bytes, sha256, bytes.byteLength, publishedAt),
  ]);
  return { version: manifest.version, sha256, size: bytes.byteLength, publishedAt };
}
