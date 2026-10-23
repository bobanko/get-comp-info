// Optional settings that are not declared in wrangler.jsonc.
interface OptionalEnv {
  // JSON Web Key Set used instead of Cloudflare's public keys (tests only).
  ACCESS_JWKS?: string;
  // Signed-in user for local `wrangler dev`, used only for requests to localhost. Set it in .dev.vars.
  DEV_AUTH_EMAIL?: string;
  // Secret: Cloudflare API token with "Access: Apps and Policies Edit", used to keep the
  // Access allow policy in step with active users. Without it emails are added by hand.
  CF_API_TOKEN?: string;
}

interface Env extends OptionalEnv {}

declare namespace Cloudflare {
  interface Env extends OptionalEnv {}
}
