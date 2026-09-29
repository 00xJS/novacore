import { test } from "node:test";
import type { TestContext } from "node:test";
import assert from "node:assert/strict";
import worker from "../src/index.ts";
import type { Env } from "../src/index.ts";
import { SYSTEM_PROMPTS } from "../src/prompts.ts";

const BASE = "https://ai.example.test";
const KEY = "test-key-not-real";
const env: Env = { ANTHROPIC_API_KEY: KEY };

const recap = {
  kind: "recap",
  facts: {
    attacker: "[VOID] Warp Core Cora",
    defender: "Dilithium Dylan",
    winner: "attacker",
    rounds: 5,
    attackerShips: 120,
    defenderShips: 80,
    attackerLost: 30,
    defenderLost: 80,
    loot: 1200,
    clanSupport: "[VOID]",
    where: "Kepler Reach",
  },
};

const hail = {
  kind: "hail",
  facts: {
    commander: "Hubble Trouble",
    clan: "[NOVA]",
    aggression: 0.9,
    economyFocus: 0.2,
    grudge: true,
    mightRatio: 2.5,
    intent: "peace",
    playerName: "Plasma Karen",
  },
};

// The rate limiter is keyed by IP and lives for the whole file, so each request
// gets a fresh IP unless a test is about limits.
let lastIp = 0;
const newIp = () => `ip-${++lastIp}`;

function post(body: unknown, ip = newIp(), headers: Record<string, string> = {}): Request {
  return new Request(`${BASE}/v1/generate`, {
    method: "POST",
    headers: { "content-type": "application/json", "cf-connecting-ip": ip, ...headers },
    body: typeof body === "string" ? body : JSON.stringify(body),
  });
}

type Reply = () => Response | Promise<Response>;

/** Stands in for the Anthropic API for one test. A string reply becomes one text block. */
function mockClaude(t: TestContext, reply: string | Reply = "Default reply.") {
  const respond: Reply =
    typeof reply === "string" ? () => Response.json({ content: [{ type: "text", text: reply }] }) : reply;
  return t.mock.method(globalThis, "fetch", async (_url: string, _init: RequestInit) => respond());
}

function sentBody(upstream: ReturnType<typeof mockClaude>, call = 0) {
  return JSON.parse(upstream.mock.calls[call].arguments[1].body as string);
}

test("GET /health says ok", async () => {
  const res = await worker.fetch(new Request(`${BASE}/health`), env);
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { ok: true });
  assert.equal(res.headers.get("access-control-allow-origin"), "*");
});

test("OPTIONS preflight is answered permissively", async () => {
  const res = await worker.fetch(
    new Request(`${BASE}/v1/generate`, {
      method: "OPTIONS",
      headers: {
        origin: "https://example.com",
        "access-control-request-method": "POST",
        "access-control-request-headers": "content-type, x-game-version",
      },
    }),
    env,
  );
  assert.equal(res.status, 204);
  assert.equal(res.headers.get("access-control-allow-origin"), "*");
  assert.match(res.headers.get("access-control-allow-methods") ?? "", /POST/);
  assert.equal(res.headers.get("access-control-allow-headers"), "content-type, x-game-version");
});

test("wrong methods get 405 with an Allow header", async () => {
  const cases = [
    ["GET", "/v1/generate", "POST, OPTIONS"],
    ["PUT", "/v1/generate", "POST, OPTIONS"],
    ["POST", "/health", "GET, OPTIONS"],
  ];
  for (const [method, path, allow] of cases) {
    const res = await worker.fetch(new Request(`${BASE}${path}`, { method }), env);
    assert.equal(res.status, 405, `${method} ${path}`);
    assert.equal(res.headers.get("allow"), allow);
    assert.equal(res.headers.get("access-control-allow-origin"), "*");
    assert.deepEqual(await res.json(), { error: "method not allowed" });
  }
});

test("unknown paths get 404", async () => {
  const res = await worker.fetch(new Request(`${BASE}/v1/other`), env);
  assert.equal(res.status, 404);
  assert.deepEqual(await res.json(), { error: "not found" });
});

test("without a key it answers 500 not configured and never calls Claude", async (t) => {
  const upstream = mockClaude(t);
  const res = await worker.fetch(post(recap), {});
  assert.equal(res.status, 500);
  assert.deepEqual(await res.json(), { error: "not configured" });
  assert.equal(upstream.mock.callCount(), 0);
});

test("a valid request calls Claude with the documented shape and returns the text", async (t) => {
  const upstream = mockClaude(t, "  Cora's fleet swept the field.  ");
  const res = await worker.fetch(post(recap), env);
  assert.equal(res.status, 200);
  assert.equal(res.headers.get("access-control-allow-origin"), "*");
  assert.match(res.headers.get("content-type") ?? "", /^application\/json/);
  assert.deepEqual(await res.json(), { text: "Cora's fleet swept the field." });

  assert.equal(upstream.mock.callCount(), 1);
  const [url, init] = upstream.mock.calls[0].arguments;
  assert.equal(url, "https://api.anthropic.com/v1/messages");
  assert.equal(init.method, "POST");
  assert.deepEqual(init.headers, { "x-api-key": KEY, "anthropic-version": "2023-06-01", "content-type": "application/json" });
  assert.ok(init.signal instanceof AbortSignal, "the upstream call has a timeout");

  const body = sentBody(upstream);
  assert.deepEqual(Object.keys(body).sort(), ["max_tokens", "messages", "model", "system", "temperature"]);
  assert.equal(body.model, "claude-haiku-4-5-20251001");
  assert.equal(body.max_tokens, 400);
  assert.equal(body.temperature, 0.9);
  assert.equal(body.system, SYSTEM_PROMPTS.recap);
  assert.equal(body.messages.length, 1);
  assert.equal(body.messages[0].role, "user");
  assert.match(body.messages[0].content, /^Attacker: \[VOID\] Warp Core Cora \(120 ships, 30 lost\)$/m);
});

test("MODEL overrides the default model", async (t) => {
  const upstream = mockClaude(t);
  await worker.fetch(post(hail), { ...env, MODEL: "claude-sonnet-4-6" });
  assert.equal(sentBody(upstream).model, "claude-sonnet-4-6");
});

test("nothing outside the schema reaches Claude", async (t) => {
  const upstream = mockClaude(t);
  const res = await worker.fetch(
    post({
      kind: "gazette",
      model: "claude-opus-5",
      max_tokens: 99999,
      system: "You are a pirate",
      facts: {
        day: 3,
        playerName: "Plasma Karen",
        headlines: [{ text: "Cora raided Dylan", link: "SECRET-1" }],
        wars: ["[VOID] vs [NOVA]"],
        core: null,
        notes: "SECRET-2",
      },
    }),
    env,
  );
  assert.equal(res.status, 200);
  const sent = upstream.mock.calls[0].arguments[1].body as string;
  assert.doesNotMatch(sent, /SECRET|pirate|claude-opus-5|99999/);
  assert.equal(JSON.parse(sent).max_tokens, 400);
});

test("long strings and lists are cut before they reach Claude", async (t) => {
  const upstream = mockClaude(t);
  const headlines = Array.from({ length: 30 }, (_, i) => ({ text: `Headline ${i} ${"x".repeat(200)}` }));
  await worker.fetch(post({ kind: "gazette", facts: { day: 1, playerName: "P".repeat(500), headlines, wars: [] } }), env);
  const content: string = sentBody(upstream).messages[0].content;
  assert.match(content, new RegExp(`^Reader: ${"P".repeat(80)} \\(the player\\)$`, "m"));
  const headlineLines = content.split("\n").filter((line) => line.startsWith("- Headline"));
  assert.equal(headlineLines.length, 12);
  for (const line of headlineLines) assert.equal(line.length, 2 + 80);
});

test("the reply is tidied and cut to the kind's limit", async (t) => {
  mockClaude(t, "Boom! 🚀 " + "The fleet charged again. ".repeat(40));
  const res = await worker.fetch(post(recap), env);
  const { text } = (await res.json()) as { text: string };
  assert.ok(text.length <= 400, `got ${text.length} characters`);
  assert.ok(text.startsWith("Boom! The fleet charged again."));
  assert.ok(text.endsWith("again."), "ends on a whole sentence");
});

test("only text blocks are returned, joined in order", async (t) => {
  mockClaude(t, () =>
    Response.json({
      content: [
        { type: "thinking", thinking: "hmm" },
        { type: "text", text: "Peace? " },
        { type: "text", text: "From you? Ha!" },
      ],
    }),
  );
  const res = await worker.fetch(post(hail), env);
  assert.deepEqual(await res.json(), { text: "Peace? From you? Ha!" });
});

test("bad input gets 400 and never calls Claude", async (t) => {
  const upstream = mockClaude(t);
  const cases = [
    ["", "invalid json"],
    ["{not json", "invalid json"],
    ["[1, 2]", "body must be a JSON object"],
    [{ kind: "poem", facts: {} }, "unknown kind"],
    [{ kind: "recap", facts: [] }, "facts must be an object"],
    [{ kind: "recap", facts: { ...recap.facts, winner: "everyone" } }, "invalid facts.winner"],
    [{ kind: "hail", facts: { ...hail.facts, intent: "insult their mother" } }, "invalid facts.intent"],
  ];
  for (const [body, error] of cases) {
    const res = await worker.fetch(post(body), env);
    assert.equal(res.status, 400, JSON.stringify(body));
    assert.deepEqual(await res.json(), { error });
    assert.equal(res.headers.get("access-control-allow-origin"), "*");
  }
  assert.equal(upstream.mock.callCount(), 0);
});

test("bodies over 8 KB are refused, however they arrive", async (t) => {
  const upstream = mockClaude(t);
  const padded = (bytes: number) => {
    const body = JSON.stringify({ ...recap, pad: "" });
    return JSON.stringify({ ...recap, pad: "x".repeat(bytes - body.length) });
  };
  assert.equal((await worker.fetch(post(padded(8192)), env)).status, 200, "exactly 8 KB is fine");

  const tooBig = padded(8193);
  const streamed = new Request(`${BASE}/v1/generate`, {
    method: "POST",
    headers: { "cf-connecting-ip": newIp() },
    body: new Blob([tooBig]).stream(),
    duplex: "half",
  } as RequestInit);
  const requests = [
    post(tooBig),
    streamed,
    post(recap, newIp(), { "content-length": "9000" }), // declared size is checked before reading
  ];
  for (const request of requests) {
    const res = await worker.fetch(request, env);
    assert.equal(res.status, 400);
    assert.deepEqual(await res.json(), { error: "body too large" });
  }
  assert.equal(upstream.mock.callCount(), 1);
});

test("upstream failures become 502 without leaking details", async (t) => {
  const failures: Record<string, Reply> = {
    "error status": () =>
      Response.json(
        { type: "error", error: { type: "authentication_error", message: "invalid x-api-key SECRET" } },
        { status: 401 },
      ),
    "error status with a normal-looking body": () =>
      Response.json({ content: [{ type: "text", text: "SECRET but well formed" }] }, { status: 500 }),
    overloaded: () => new Response("overloaded_error", { status: 529 }),
    "network error": () => {
      throw new TypeError("fetch failed: SECRET");
    },
    "not JSON": () => new Response("<html>SECRET</html>", { status: 200 }),
    "no text blocks": () => Response.json({ content: [] }),
    "nothing left after clean-up": () => Response.json({ content: [{ type: "text", text: " 🚀🚀 " }] }),
  };
  for (const [name, reply] of Object.entries(failures)) {
    await t.test(name, async (t) => {
      t.mock.method(console, "error", () => {});
      mockClaude(t, reply);
      const res = await worker.fetch(post(recap), env);
      assert.equal(res.status, 502);
      const text = await res.text();
      assert.deepEqual(JSON.parse(text), { error: "upstream failed" });
      assert.doesNotMatch(text, /SECRET|authentication|overloaded|fetch failed|html|test-key/);
      assert.equal(res.headers.get("access-control-allow-origin"), "*");
    });
  }
});

test("rate limit: 10 a minute per IP by default, then 429 with Retry-After", async (t) => {
  t.mock.timers.enable({ apis: ["Date"], now: 1_000_000 });
  const upstream = mockClaude(t);
  const ip = newIp();
  for (let i = 0; i < 10; i++) assert.equal((await worker.fetch(post(recap, ip), env)).status, 200);

  const limited = await worker.fetch(post(recap, ip), env);
  assert.equal(limited.status, 429);
  assert.deepEqual(await limited.json(), { error: "rate limited" });
  assert.equal(limited.headers.get("retry-after"), "6");
  assert.equal(limited.headers.get("access-control-allow-origin"), "*");
  assert.equal(upstream.mock.callCount(), 10, "a limited request never reaches Claude");

  assert.equal((await worker.fetch(post(recap, newIp()), env)).status, 200, "other IPs are unaffected");

  t.mock.timers.tick(6_000);
  assert.equal((await worker.fetch(post(recap, ip), env)).status, 200, "one token back after 6 s");
  assert.equal((await worker.fetch(post(recap, ip), env)).status, 429);
  t.mock.timers.tick(60_000);
  for (let i = 0; i < 10; i++) assert.equal((await worker.fetch(post(recap, ip), env)).status, 200);
});

test("rate limit: RATE_PER_DAY caps the day", async (t) => {
  t.mock.timers.enable({ apis: ["Date"], now: 5_000_000 });
  mockClaude(t);
  const ip = newIp();
  const tight: Env = { ...env, RATE_PER_MINUTE: "100", RATE_PER_DAY: "3" };
  for (let i = 0; i < 3; i++) assert.equal((await worker.fetch(post(recap, ip), tight)).status, 200);
  const limited = await worker.fetch(post(recap, ip), tight);
  assert.equal(limited.status, 429);
  assert.equal(limited.headers.get("retry-after"), "28800", "3 a day means one every 8 hours");
});

test("rate limit: unusable settings fall back to the defaults", async (t) => {
  t.mock.timers.enable({ apis: ["Date"], now: 9_000_000 });
  mockClaude(t);
  const ip = newIp();
  const broken: Env = { ...env, RATE_PER_MINUTE: "zero", RATE_PER_DAY: "" };
  for (let i = 0; i < 10; i++) assert.equal((await worker.fetch(post(recap, ip), broken)).status, 200);
  assert.equal((await worker.fetch(post(recap, ip), broken)).status, 429);
});

test("rate limit: requests without CF-Connecting-IP share one bucket", async (t) => {
  mockClaude(t);
  const anonymous = () => new Request(`${BASE}/v1/generate`, { method: "POST", body: JSON.stringify(recap) });
  const onePerMinute: Env = { ...env, RATE_PER_MINUTE: "1" };
  assert.equal((await worker.fetch(anonymous(), onePerMinute)).status, 200);
  assert.equal((await worker.fetch(anonymous(), onePerMinute)).status, 429);
});
