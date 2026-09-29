// Galaxy Royale AI proxy: a Cloudflare Worker that turns game facts into short
// flavour text with Claude. The API key lives here as a secret, never in the app.
import { parseRequest } from "./facts.ts";
import { SYSTEM_PROMPTS, buildUserMessage, finishText } from "./prompts.ts";

export interface Env {
  ANTHROPIC_API_KEY?: string;
  MODEL?: string;
  RATE_PER_MINUTE?: string;
  RATE_PER_DAY?: string;
}

const ANTHROPIC_URL = "https://api.anthropic.com/v1/messages";
const DEFAULT_MODEL = "claude-haiku-4-5-20251001";
const DEFAULT_PER_MINUTE = 10;
const DEFAULT_PER_DAY = 200;
const MAX_BODY_BYTES = 8 * 1024;
const UPSTREAM_TIMEOUT_MS = 20_000;

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    if (request.method === "OPTIONS") return preflight(request);
    const { pathname } = new URL(request.url);
    try {
      if (pathname === "/health") {
        return request.method === "GET" ? json(200, { ok: true }) : notAllowed("GET, OPTIONS");
      }
      if (pathname === "/v1/generate") {
        return request.method === "POST" ? await generate(request, env) : notAllowed("POST, OPTIONS");
      }
      return json(404, { error: "not found" });
    } catch (err) {
      console.error("unhandled error:", err);
      return json(500, { error: "internal error" });
    }
  },
};

async function generate(request: Request, env: Env): Promise<Response> {
  const apiKey = env.ANTHROPIC_API_KEY;
  if (!apiKey) return json(500, { error: "not configured" });

  const ip = request.headers.get("cf-connecting-ip") || "anon";
  const perMinute = positiveInt(env.RATE_PER_MINUTE, DEFAULT_PER_MINUTE);
  const perDay = positiveInt(env.RATE_PER_DAY, DEFAULT_PER_DAY);
  const wait = takeToken(ip, perMinute, perDay, Date.now());
  if (wait > 0) return json(429, { error: "rate limited" }, { "retry-after": String(wait) });

  const raw = await readBody(request, MAX_BODY_BYTES);
  if (raw === null) return json(400, { error: "body too large" });
  let body: unknown;
  try {
    body = JSON.parse(raw);
  } catch {
    return json(400, { error: "invalid json" });
  }
  const req = parseRequest(body);
  if ("error" in req) return json(400, { error: req.error });

  const reply = await askClaude(apiKey, env.MODEL || DEFAULT_MODEL, SYSTEM_PROMPTS[req.kind], buildUserMessage(req));
  const text = reply === null ? "" : finishText(req.kind, reply);
  if (!text) return json(502, { error: "upstream failed" });
  return json(200, { text });
}

/** Claude's reply text, or null if the call failed in any way. */
async function askClaude(apiKey: string, model: string, system: string, content: string): Promise<string | null> {
  try {
    const res = await fetch(ANTHROPIC_URL, {
      method: "POST",
      headers: { "x-api-key": apiKey, "anthropic-version": "2023-06-01", "content-type": "application/json" },
      body: JSON.stringify({ model, max_tokens: 400, temperature: 0.9, system, messages: [{ role: "user", content }] }),
      signal: AbortSignal.timeout(UPSTREAM_TIMEOUT_MS),
    });
    if (!res.ok) {
      // For `wrangler tail` only; the game just sees "upstream failed".
      console.error(`upstream ${res.status}: ${(await res.text()).slice(0, 300)}`);
      return null;
    }
    const data = (await res.json()) as { content?: unknown };
    const blocks = Array.isArray(data?.content) ? (data.content as { type?: unknown; text?: unknown }[]) : [];
    return blocks.map((b) => (b?.type === "text" && typeof b.text === "string" ? b.text : "")).join("");
  } catch (err) {
    console.error("upstream call failed:", err instanceof Error ? err.message : String(err));
    return null;
  }
}

/** Reads at most maxBytes of the body; null if it is bigger. */
async function readBody(request: Request, maxBytes: number): Promise<string | null> {
  if (Number(request.headers.get("content-length")) > maxBytes) return null;
  if (!request.body) return "";
  const reader = request.body.getReader();
  const decoder = new TextDecoder();
  let size = 0;
  let text = "";
  for (;;) {
    const { done, value } = await reader.read();
    if (done) return text + decoder.decode();
    size += value.byteLength;
    if (size > maxBytes) {
      await reader.cancel();
      return null;
    }
    text += decoder.decode(value, { stream: true });
  }
}

// Rate limiting: two token buckets per client IP, one per minute and one per day.
// They live in this isolate's memory, so they are best-effort: Cloudflare runs
// many isolates, each with its own counts, and a recycled isolate starts afresh.
interface Bucket {
  tokens: number;
  at: number;
}

const MINUTE_MS = 60_000;
const DAY_MS = 86_400_000;
const MAX_TRACKED_IPS = 10_000;
const clients = new Map<string, { minute: Bucket; day: Bucket }>();

/** Spends one request for this IP. Returns 0 if allowed, else the seconds to wait. */
function takeToken(ip: string, perMinute: number, perDay: number, now: number): number {
  let client = clients.get(ip);
  if (client) {
    clients.delete(ip); // re-added below, so the Map stays ordered by last use
  } else {
    client = { minute: { tokens: perMinute, at: now }, day: { tokens: perDay, at: now } };
  }
  clients.set(ip, client);
  if (clients.size > MAX_TRACKED_IPS) clients.delete(clients.keys().next().value!);

  const wait = Math.max(refill(client.minute, perMinute, MINUTE_MS, now), refill(client.day, perDay, DAY_MS, now));
  if (wait > 0) return wait;
  client.minute.tokens -= 1;
  client.day.tokens -= 1;
  return 0;
}

/** Adds the tokens earned since the last visit. Returns 0 if a whole token is ready, else the seconds until one is. */
function refill(bucket: Bucket, capacity: number, periodMs: number, now: number): number {
  const earned = (Math.max(0, now - bucket.at) * capacity) / periodMs;
  bucket.tokens = Math.min(capacity, bucket.tokens + earned);
  bucket.at = now;
  return bucket.tokens >= 1 ? 0 : Math.ceil(((1 - bucket.tokens) * periodMs) / capacity / 1000);
}

function positiveInt(value: string | undefined, fallback: number): number {
  const n = Number(value);
  return Number.isFinite(n) && n >= 1 ? Math.floor(n) : fallback;
}

function json(status: number, body: unknown, extraHeaders: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "access-control-allow-origin": "*",
      ...extraHeaders,
    },
  });
}

function notAllowed(allow: string): Response {
  return json(405, { error: "method not allowed" }, { allow });
}

function preflight(request: Request): Response {
  return new Response(null, {
    status: 204,
    headers: {
      "access-control-allow-origin": "*",
      "access-control-allow-methods": "GET, POST, OPTIONS",
      "access-control-allow-headers": request.headers.get("access-control-request-headers") || "content-type",
      "access-control-max-age": "86400",
    },
  });
}
