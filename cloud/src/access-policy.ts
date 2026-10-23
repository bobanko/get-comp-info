// Keeps the Cloudflare Access allow policy in step with active users, so a user added
// or re-enabled in the web interface can sign in right away. Optional: without
// CF_API_TOKEN the admin adds emails to the policy by hand.

export type AccessSyncResult = { synced: true } | { synced: false; reason: string };

type Fetcher = (input: string, init?: RequestInit) => Promise<Response>;

interface PolicyRule {
  email?: { email: string };
  [key: string]: unknown;
}

interface Policy {
  name: string;
  decision: string;
  include: PolicyRule[];
  exclude?: unknown[];
  require?: unknown[];
  session_duration?: string;
}

export async function syncAccessPolicy(env: Env, fetcher: Fetcher = fetch): Promise<AccessSyncResult> {
  if (!env.CF_API_TOKEN || !env.CF_ACCOUNT_ID || !env.ACCESS_POLICY_ID) {
    return { synced: false, reason: 'not_configured' };
  }
  const { results } = await env.DB.prepare('SELECT email FROM users WHERE active = 1 ORDER BY email').all<{
    email: string;
  }>();
  if (results.length === 0) {
    // An Access policy cannot be empty, and an empty list would lock everyone out anyway.
    return { synced: false, reason: 'no_active_users' };
  }

  const url = `https://api.cloudflare.com/client/v4/accounts/${env.CF_ACCOUNT_ID}/access/policies/${env.ACCESS_POLICY_ID}`;
  const headers = { Authorization: `Bearer ${env.CF_API_TOKEN}`, 'Content-Type': 'application/json' };
  try {
    const current = await fetcher(url, { headers });
    if (!current.ok) return { synced: false, reason: await describeFailure(current) };
    const policy = ((await current.json()) as { result: Policy }).result;

    // Email rules are replaced by the active users; any other kind of rule is kept as is.
    const include = [
      ...policy.include.filter((rule) => rule.email === undefined),
      ...results.map((row) => ({ email: { email: row.email } })),
    ];
    const updated = await fetcher(url, {
      method: 'PUT',
      headers,
      body: JSON.stringify({
        name: policy.name,
        decision: policy.decision,
        include,
        exclude: policy.exclude ?? [],
        require: policy.require ?? [],
        session_duration: policy.session_duration,
      }),
    });
    if (!updated.ok) return { synced: false, reason: await describeFailure(updated) };
    return { synced: true };
  } catch (error) {
    return { synced: false, reason: String(error) };
  }
}

async function describeFailure(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { errors?: { message: string }[] };
    const message = body.errors?.map((error) => error.message).join('; ');
    if (message) return `HTTP ${response.status}: ${message}`;
  } catch {
    // Not JSON: fall back to the status.
  }
  return `HTTP ${response.status}`;
}
