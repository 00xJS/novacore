// The only input the proxy accepts. Unknown fields are dropped, strings are
// flattened to one line and cut to 80 characters, lists are cut to their max
// length and numbers are coerced and bounded, so nothing outside this shape
// can reach the prompt.

export const KINDS = ["gazette", "recap", "hail"] as const;
export type Kind = (typeof KINDS)[number];

export const MAX_STRING = 80;
export const MAX_HEADLINES = 12;
export const MAX_WARS = 5;
const MAX_COUNT = 1_000_000_000;

export interface GazetteFacts {
  day: number;
  playerName: string;
  headlines: { text: string }[];
  wars: string[];
  core: string | null;
}

export interface RecapFacts {
  attacker: string;
  defender: string;
  winner: "attacker" | "defender" | "draw";
  rounds: number;
  attackerShips: number;
  defenderShips: number;
  attackerLost: number;
  defenderLost: number;
  loot: number;
  clanSupport: string | null;
  where: string;
}

export interface HailFacts {
  commander: string;
  clan: string | null;
  aggression: number;
  economyFocus: number;
  grudge: boolean;
  mightRatio: number;
  intent: "taunt" | "peace" | "clan" | "trade";
  playerName: string;
}

interface FactsByKind {
  gazette: GazetteFacts;
  recap: RecapFacts;
  hail: HailFacts;
}

export type GenerateRequest = {
  [K in Kind]: { kind: K; facts: FactsByKind[K]; seed?: number };
}[Kind];

type Raw = Record<string, unknown>;

class BadInput extends Error {}

const SANITIZE: { [K in Kind]: (f: Raw) => FactsByKind[K] } = {
  gazette: (f) => ({
    day: int(f.day, 0, 1_000_000),
    playerName: str(f.playerName),
    headlines: list(f.headlines)
      .map((h) => ({ text: isRecord(h) ? str(h.text) : "" }))
      .filter((h) => h.text)
      .slice(0, MAX_HEADLINES),
    wars: list(f.wars).map(str).filter(Boolean).slice(0, MAX_WARS),
    core: strOrNull(f.core),
  }),
  recap: (f) => ({
    attacker: str(f.attacker),
    defender: str(f.defender),
    winner: oneOf(f.winner, ["attacker", "defender", "draw"] as const, "winner"),
    rounds: int(f.rounds, 0, 1000),
    attackerShips: int(f.attackerShips, 0, MAX_COUNT),
    defenderShips: int(f.defenderShips, 0, MAX_COUNT),
    attackerLost: int(f.attackerLost, 0, MAX_COUNT),
    defenderLost: int(f.defenderLost, 0, MAX_COUNT),
    loot: int(f.loot, 0, MAX_COUNT * 1000),
    clanSupport: strOrNull(f.clanSupport),
    where: str(f.where),
  }),
  hail: (f) => ({
    commander: str(f.commander),
    clan: strOrNull(f.clan),
    aggression: num(f.aggression, 0, 1, 0.5),
    economyFocus: num(f.economyFocus, 0, 1, 0.5),
    grudge: f.grudge === true || f.grudge === "true" || f.grudge === 1,
    mightRatio: num(f.mightRatio, 0, 1000, 1),
    intent: oneOf(f.intent, ["taunt", "peace", "clan", "trade"] as const, "intent"),
    playerName: str(f.playerName),
  }),
};

/** Turns a parsed JSON body into a clean request, or a short error for a 400. */
export function parseRequest(body: unknown): GenerateRequest | { error: string } {
  if (!isRecord(body)) return { error: "body must be a JSON object" };
  const kind = body.kind as Kind;
  if (!KINDS.includes(kind)) return { error: "unknown kind" };
  if (!isRecord(body.facts)) return { error: "facts must be an object" };

  try {
    const req = { kind, facts: SANITIZE[kind](body.facts) } as GenerateRequest;
    const seed = toNumber(body.seed);
    if (Number.isFinite(seed)) req.seed = Math.trunc(seed);
    return req;
  } catch (err) {
    if (err instanceof BadInput) return { error: err.message };
    throw err;
  }
}

function isRecord(v: unknown): v is Raw {
  return typeof v === "object" && v !== null && !Array.isArray(v);
}

function str(v: unknown): string {
  if (typeof v !== "string" && typeof v !== "number") return "";
  // One line only, so a value cannot pose as another line of the prompt.
  const flat = String(v).replace(/[\s\u0000-\u001f\u007f-\u009f]+/g, " ").trim();
  if (flat.length <= MAX_STRING) return flat;
  return flat.slice(0, MAX_STRING).replace(/[\uD800-\uDBFF]$/, "").trimEnd();
}

function strOrNull(v: unknown): string | null {
  return str(v) || null;
}

function list(v: unknown): unknown[] {
  return Array.isArray(v) ? v : [];
}

function toNumber(v: unknown): number {
  if (typeof v === "number") return v;
  if (typeof v === "string" && v.trim() !== "") return Number(v);
  return NaN;
}

function num(v: unknown, min: number, max: number, fallback: number): number {
  const n = toNumber(v);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
}

function int(v: unknown, min: number, max: number): number {
  return Math.trunc(num(v, min, max, min));
}

function oneOf<T extends string>(v: unknown, values: readonly T[], field: string): T {
  if (values.includes(v as T)) return v as T;
  throw new BadInput(`invalid facts.${field}`);
}
