# Galaxy Royale AI proxy

A small Cloudflare Worker that writes short flavour text for Galaxy Royale with Claude.

The game sends plain game facts, such as a battle result. The worker builds the prompt, calls Claude and sends back the text.
The Anthropic API key lives only in the worker, as an encrypted secret. It is never in the app.

It writes three kinds of text:

- **gazette**: the daily galaxy newspaper.
- **recap**: a war correspondent's report on one battle.
- **hail**: a commander's in-character reply when the player hails them.

## Deploy

You need a Cloudflare account (the free plan is fine), an Anthropic API key and Node.js 22 or newer.

1. Open a terminal in this folder (`server/ai-proxy`).
2. Log in to Cloudflare: `npx wrangler login`
3. Deploy: `npx wrangler deploy`
   The first time, Cloudflare may ask you to pick a `workers.dev` subdomain.
   It prints the worker URL, for example `https://galaxy-royale-ai.your-name.workers.dev`.
4. Add the key: `npx wrangler secret put ANTHROPIC_API_KEY`
   Paste the key when asked.
5. Check it is up: open `https://galaxy-royale-ai.your-name.workers.dev/health`. You should see `{"ok":true}`.
6. Try a real call:

   ```sh
   curl -X POST https://galaxy-royale-ai.your-name.workers.dev/v1/generate \
     -H 'content-type: application/json' \
     -d '{"kind":"hail","facts":{"commander":"Hubble Trouble","clan":"[NOVA]","aggression":0.8,"economyFocus":0.3,"grudge":true,"mightRatio":1.6,"intent":"peace","playerName":"Plasma Karen"}}'
   ```

7. In the game, open Settings › AI WRITERS, paste the worker URL and tap SAVE.
   Then tap TEST: it should say it's connected.

To change a setting, edit `wrangler.toml` and run `npx wrangler deploy` again.
To replace the key, run `npx wrangler secret put ANTHROPIC_API_KEY` again.
To watch live logs, run `npx wrangler tail`. Upstream errors show there; the key is never logged.

## Settings

| Name | Where | Default | What it does |
| --- | --- | --- | --- |
| `ANTHROPIC_API_KEY` | secret | none | Your Anthropic key. Without it, every generate call returns 500 `not configured`. |
| `MODEL` | `wrangler.toml` | `claude-haiku-4-5-20251001` | The Claude model. |
| `RATE_PER_MINUTE` | `wrangler.toml` | `10` | Requests per minute from one IP address. |
| `RATE_PER_DAY` | `wrangler.toml` | `200` | Requests per day from one IP address. |

The worker sends `temperature: 0.9`.
Haiku 4.5 accepts it, and so do Sonnet 4.6 and Opus 4.6.
Newer models (Sonnet 5, Opus 4.7 and later) reject it, so every call would fail with 502. Keep that in mind if you change `MODEL`.

## Cost and abuse

- Once the app ships, the worker URL is public. Anyone who finds it can call it.
- Keep the per-IP limits on. They slow down any one device that tries to run up your bill.
- The limits are kept in memory, so they are best-effort. Cloudflare runs many copies of the worker, each with its own counts, and a copy that restarts forgets them.
- Set a monthly spend limit in the Anthropic console. That is the real safety net.
  A separate workspace and key just for the game makes this easy, and lets you revoke the key without touching anything else.
- Each call is small: about 400 tokens in and at most 400 out.
  With Haiku 4.5 that is roughly a tenth of a cent per call, so one IP at the daily limit costs about 20 to 50 cents a day. Prices change, so check Anthropic's pricing page.
- It makes a poor free chatbot for strangers: it only accepts the three kinds below, cuts every string to 80 characters and caps every reply.
- If you see abuse, lower the limits, add a Cloudflare rate limiting rule, or replace the key.

## API

Every response has `Access-Control-Allow-Origin: *`. `OPTIONS` requests (CORS preflight) get a 204 on any path.

### `GET /health`

Returns `{ "ok": true }`.

### `POST /v1/generate`

```json
{ "kind": "gazette", "facts": { ... }, "seed": 42 }
```

- `kind` is `"gazette"`, `"recap"` or `"hail"`.
- `facts` must be an object. Its fields depend on the kind (see below).
- `seed` is an optional integer. It picks the writing tone, such as "breathless tabloid". The same seed always gives the same tone. Without it, Claude picks.

The body can be at most 8 KB.
Fields that are not listed here are dropped and never reach Claude.
Strings are flattened to one line and cut to 80 characters.
Lists are cut to their maximum length.
Numbers are coerced (`"12"` becomes `12`) and kept in a sensible range. Missing numbers become 0 unless noted below.
Missing strings become empty, or null where null is allowed.

A success returns `{ "text": "..." }`.
The text is plain: no emoji and no markdown, with straight quotes and plain hyphens. The gazette bullet `•` is the one non-ASCII character it asks for.
Long text is cut at the end of a sentence or line where possible.

Errors return `{ "error": "short message" }`:

| Status | When |
| --- | --- |
| 400 | Bad input: body over 8 KB, invalid JSON, unknown `kind`, `facts` not an object, or a bad `winner` or `intent`. |
| 404 | Unknown path. |
| 405 | Wrong method. |
| 429 | Rate limited. The `Retry-After` header gives the seconds to wait. Every generate call counts, even a bad one. |
| 500 | Not configured: the key is missing. |
| 502 | Claude failed, timed out (20 seconds) or sent back nothing usable. |

The game should treat any status other than 200 as "use the built-in text".

### `gazette`: the daily newspaper

| Field | Type | Notes |
| --- | --- | --- |
| `day` | integer | Game day. |
| `playerName` | string | The player's commander name. |
| `headlines` | list of `{ "text": string }` | Up to 12. |
| `wars` | list of strings | Up to 5, for example `"[VOID] vs [NOVA]"`. |
| `core` | string or null | News about the Galactic Core. |

Reply: a punchy headline on the first line, then 3 to 5 short items, each on its own line starting with `• `. At most 700 characters.

### `recap`: one battle

| Field | Type | Notes |
| --- | --- | --- |
| `attacker`, `defender` | string | Commander names, with clan tags if you like. |
| `winner` | `"attacker"`, `"defender"` or `"draw"` | Anything else is a 400. |
| `rounds` | integer | |
| `attackerShips`, `defenderShips` | integer | Ships at the start. |
| `attackerLost`, `defenderLost` | integer | Ships knocked out. |
| `loot` | integer | Total gold, quartz and helium taken. 0 for none. |
| `clanSupport` | string or null | Clan help in the fight. |
| `where` | string | Where it happened. |

Reply: 2 to 3 vivid sentences in one paragraph. At most 400 characters.

### `hail`: a commander answers the player

| Field | Type | Notes |
| --- | --- | --- |
| `commander` | string | The commander's name. |
| `clan` | string or null | Their clan. |
| `aggression` | number, 0 to 1 | Missing means 0.5. |
| `economyFocus` | number, 0 to 1 | Missing means 0.5. |
| `grudge` | boolean | Do they hold a grudge against the player? |
| `mightRatio` | number | Their might divided by the player's. Missing means 1. |
| `intent` | `"taunt"`, `"peace"`, `"clan"` or `"trade"` | What the player hailed about. Anything else is a 400. |
| `playerName` | string | The player's commander name. |

Reply: 1 to 2 sentences in the commander's voice, at most 280 characters.
Claude is asked to show how the commander feels without accepting or refusing outright, because the game decides the outcome.

## Development

- Tests: `npm test` (the same as `node --test test/*.test.ts`). Node 22.18 or newer runs the TypeScript directly, so there is nothing to install.
- Local server: put `ANTHROPIC_API_KEY=your-key` in a file called `.dev.vars`, then run `npx wrangler dev`. The file is git-ignored; never commit it.
- Code: `src/index.ts` is the worker (routes, limits, the Claude call). `src/facts.ts` checks and cleans the input. `src/prompts.ts` holds the prompts and tidies the reply.
