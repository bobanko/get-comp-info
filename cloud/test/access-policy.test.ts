import { env } from 'cloudflare:test';
import { describe, expect, it } from 'vitest';
import { syncAccessPolicy } from '../src/access-policy';
import { addUser, unique, webRequest } from './helpers';

const configured = { ...env, CF_API_TOKEN: 'token', CF_ACCOUNT_ID: 'acc', ACCESS_POLICY_ID: 'pol' } as Env;

function fakeCloudflare(policy: Record<string, unknown>, putStatus = 200) {
  const calls: { method: string; url: string; body?: any; auth: string | null }[] = [];
  const fetcher = async (url: string, init: RequestInit = {}) => {
    const method = init.method ?? 'GET';
    const headers = new Headers(init.headers);
    calls.push({ method, url, body: init.body ? JSON.parse(String(init.body)) : undefined, auth: headers.get('Authorization') });
    if (method === 'GET') return Response.json({ success: true, result: policy });
    return Response.json(
      putStatus === 200 ? { success: true, result: {} } : { success: false, errors: [{ message: 'denied' }] },
      { status: putStatus },
    );
  };
  return { calls, fetcher };
}

async function activeEmails(): Promise<string[]> {
  const { results } = await env.DB.prepare('SELECT email FROM users WHERE active = 1 ORDER BY email').all<{ email: string }>();
  return results.map((row) => row.email);
}

describe('Access policy sync', () => {
  it('does nothing without an API token', async () => {
    expect(await syncAccessPolicy(env)).toEqual({ synced: false, reason: 'not_configured' });
  });

  it('replaces email rules with active users and keeps other rules', async () => {
    const active = await addUser('viewer');
    const disabled = await addUser('editor', { active: false });
    const { calls, fetcher } = fakeCloudflare({
      name: 'Comp Info team',
      decision: 'allow',
      include: [{ email: { email: 'gone@example.com' } }, { email_domain: { domain: 'chagear.top' } }],
      exclude: [],
      require: [],
      session_duration: '24h',
    });

    expect(await syncAccessPolicy(configured, fetcher)).toEqual({ synced: true });
    expect(calls.map((call) => call.method)).toEqual(['GET', 'PUT']);
    expect(calls[0].url).toBe('https://api.cloudflare.com/client/v4/accounts/acc/access/policies/pol');
    expect(calls[1].auth).toBe('Bearer token');

    const put = calls[1].body;
    expect(put).toMatchObject({ name: 'Comp Info team', decision: 'allow', session_duration: '24h' });
    expect(put.include[0]).toEqual({ email_domain: { domain: 'chagear.top' } });
    const emails = put.include.slice(1).map((rule: { email: { email: string } }) => rule.email.email);
    expect(emails).toEqual(await activeEmails());
    expect(emails).toContain(active);
    expect(emails).not.toContain(disabled);
    expect(emails).not.toContain('gone@example.com');
  });

  it('reports Cloudflare errors instead of throwing', async () => {
    await addUser('admin');
    const { fetcher } = fakeCloudflare({ name: 'p', decision: 'allow', include: [] }, 403);
    expect(await syncAccessPolicy(configured, fetcher)).toEqual({ synced: false, reason: 'HTTP 403: denied' });
    const broken = async () => {
      throw new Error('offline');
    };
    expect(await syncAccessPolicy(configured, broken)).toEqual({ synced: false, reason: 'Error: offline' });
  });

  it('tells the admin when the policy was not updated', async () => {
    const admin = await addUser('admin');
    const email = `${unique('new-user')}@example.com`;
    const created = await webRequest(admin, 'POST', '/users', { email, name: 'New', role: 'viewer' });
    expect(created.body.access).toEqual({ synced: false, reason: 'not_configured' });

    const path = `/users/${encodeURIComponent(email)}`;
    const renamed = await webRequest(admin, 'PATCH', path, { name: 'Renamed' });
    expect(renamed.body.access).toBeUndefined();
    const disabled = await webRequest(admin, 'PATCH', path, { active: false });
    expect(disabled.body.access).toEqual({ synced: false, reason: 'not_configured' });
  });
});
