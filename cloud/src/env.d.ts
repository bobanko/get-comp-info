// Optional settings that are not declared in wrangler.jsonc.
interface OptionalEnv {
  // JSON Web Key Set used instead of Cloudflare's public keys (tests only).
  ACCESS_JWKS?: string;
  // Signed-in user for local `wrangler dev` when ACCESS_AUD is empty. Set it in .dev.vars.
  DEV_AUTH_EMAIL?: string;
}

interface Env extends OptionalEnv {}

declare namespace Cloudflare {
  interface Env extends OptionalEnv {}
}
