import { env } from 'cloudflare:test';
import { exports } from 'cloudflare:workers';
import { importJWK, SignJWT, type JWK } from 'jose';
import { describe, expect, it } from 'vitest';
import { sha256Hex } from '../src/crypto';
import { addUser, webRequest } from './helpers';
import { buildRelease } from './zip-builder';

let counter = Math.floor(Math.random() * 5000);
function nextVersion(): string {
  counter++;
  return `2026.11.12.${counter}`;
}

async function githubToken(version: string): Promise<string> {
  const jwk = JSON.parse(env.TEST_GITHUB_PRIVATE_JWK) as JWK;
  return new SignJWT({ repository: 'bobanko/get-comp-info', ref_type: 'tag', ref: `refs/tags/suspector-v${version}` })
    .setProtectedHeader({ alg: 'RS256', kid: 'github-test' })
    .setIssuer('https://token.actions.githubusercontent.com')
    .setAudience('comp-info-release')
    .setIssuedAt()
    .setExpirationTime('10m')
    .sign(await importJWK(jwk, 'RS256'));
}

async function publish(version: string, files: Record<string, string> = { 'SUSpector.exe': 'MZ standalone' }) {
  const bytes = await buildRelease(version, files);
  const response = await exports.default.fetch(
    new Request('https://comp-info.test/api/suspector-release', {
      method: 'POST',
      headers: { Authorization: `Bearer ${await githubToken(version)}`, 'Content-Type': 'application/zip' },
      body: bytes,
    }),
  );
  return { response, bytes, body: (await response.clone().json()) as any };
}

describe('standalone SUSpector releases', () => {
  it('publishes only SUSpector.exe through signed tag builds', async () => {
    const version = nextVersion();
    const result = await publish(version);
    expect(result.response.status).toBe(201);
    expect(result.body).toMatchObject({ version, sha256: await sha256Hex(result.bytes) });

    const badVersion = nextVersion();
    const bad = await publish(badVersion, { 'comp-info.exe': 'MZ internal toolbox' });
    expect(bad.response.status).toBe(400);
    expect(bad.body.error.code).toBe('invalid_manifest');
  });

  it('does not accept unsigned publication', async () => {
    const version = nextVersion();
    const bytes = await buildRelease(version, { 'SUSpector.exe': 'MZ' });
    const response = await exports.default.fetch(
      new Request('https://comp-info.test/api/suspector-release', { method: 'POST', body: bytes }),
    );
    expect(response.status).toBe(401);
  });

  it('serves current metadata and package publicly without a Comp Info key', async () => {
    const version = nextVersion();
    const published = await publish(version);
    const latest = await exports.default.fetch(new Request('https://comp-info.test/api/suspector/latest'));
    expect(latest.status).toBe(200);
    expect(await latest.json()).toMatchObject({ version, size: published.bytes.byteLength, isCurrent: true });

    const packageResponse = await exports.default.fetch(
      new Request(`https://comp-info.test/api/suspector/${version}/package`),
    );
    expect(packageResponse.status).toBe(200);
    expect(await sha256Hex(new Uint8Array(await packageResponse.arrayBuffer()))).toBe(await sha256Hex(published.bytes));
    expect(packageResponse.headers.get('X-Content-SHA256')).toBe(await sha256Hex(published.bytes));
    expect((await exports.default.fetch(new Request('https://comp-info.test/api/suspector/1999.1.1.1/package'))).status).toBe(404);
  });

  it('downloads the current SUSpector.exe itself for the public page', async () => {
    const version = nextVersion();
    expect((await publish(version, { 'SUSpector.exe': 'MZ download me' })).response.status).toBe(201);
    const response = await exports.default.fetch(new Request('https://comp-info.test/api/suspector/download'));
    expect(response.status).toBe(200);
    expect(response.headers.get('Content-Disposition')).toBe('attachment; filename="SUSpector.exe"');
    expect(response.headers.get('X-SUSpector-Version')).toBe(version);
    expect(new TextDecoder().decode(await response.arrayBuffer())).toBe('MZ download me');
  });

  it('lets only admins list SUSpector versions and roll back', async () => {
    const admin = await addUser('admin');
    const editor = await addUser('editor');
    const older = nextVersion();
    const newer = nextVersion();
    await publish(older);
    await publish(newer);

    expect((await webRequest(editor, 'GET', '/suspector-releases')).status).toBe(403);
    const list = await webRequest(admin, 'GET', '/suspector-releases');
    expect(list.status).toBe(200);
    const versions = list.body.releases.map((release: any) => release.version);
    expect(versions).toContain(older);
    expect(list.body.releases.find((release: any) => release.version === newer).isCurrent).toBe(true);

    expect((await webRequest(editor, 'POST', `/suspector-releases/${older}/make-current`)).status).toBe(403);
    const rolledBack = await webRequest(admin, 'POST', `/suspector-releases/${older}/make-current`);
    expect(rolledBack.status).toBe(200);
    expect(rolledBack.body.current.version).toBe(older);
    const latest = await exports.default.fetch(new Request('https://comp-info.test/api/suspector/latest'));
    expect(((await latest.json()) as any).version).toBe(older);
    expect((await webRequest(admin, 'POST', '/suspector-releases/1999.01.01.1/make-current')).status).toBe(404);
  });
});
