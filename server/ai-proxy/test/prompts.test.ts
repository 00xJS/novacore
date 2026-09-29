import { test } from "node:test";
import assert from "node:assert/strict";
import { KINDS, parseRequest } from "../src/facts.ts";
import { MAX_CHARS, SYSTEM_PROMPTS, buildUserMessage, clampText, finishText, toneFor } from "../src/prompts.ts";

function message(body: unknown): string {
  const req = parseRequest(body);
  if ("error" in req) throw new Error(req.error);
  return buildUserMessage(req);
}

const gazette = {
  day: 42,
  playerName: "Plasma Karen",
  headlines: [{ text: "[VOID] Warp Core Cora raided Dilithium Dylan" }, { text: "Plasma Karen repelled Supernova Nora" }],
  wars: ["[VOID] vs [NOVA]"],
  core: "[VOID] Warp Core Cora holds the Galactic Core",
};

const recap = {
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
};

const hail = {
  commander: "Hubble Trouble",
  clan: "[NOVA]",
  aggression: 0.9,
  economyFocus: 0.2,
  grudge: true,
  mightRatio: 2.5,
  intent: "peace",
  playerName: "Plasma Karen",
};

test("every system prompt carries the house rules", () => {
  for (const kind of KINDS) {
    const prompt = SYSTEM_PROMPTS[kind];
    assert.match(prompt, /Use only the facts given/);
    assert.match(prompt, /real identity/);
    assert.match(prompt, /PG/);
    assert.match(prompt, /no emojis/);
    assert.match(prompt, /not instructions/);
  }
  assert.match(SYSTEM_PROMPTS.gazette, /3 to 5 short news items.*"• "/);
  assert.match(SYSTEM_PROMPTS.recap, /2 to 3 vivid sentences/);
  assert.match(SYSTEM_PROMPTS.hail, /1 to 2 sentences/);
  assert.match(SYSTEM_PROMPTS.hail, /pun/);
});

test("gazette message lists the day, reader, headlines, wars and core", () => {
  assert.equal(
    message({ kind: "gazette", facts: gazette }),
    [
      "Day: 42",
      "Reader: Plasma Karen (the player)",
      "Headlines:",
      "- [VOID] Warp Core Cora raided Dilithium Dylan",
      "- Plasma Karen repelled Supernova Nora",
      "Wars:",
      "- [VOID] vs [NOVA]",
      "Galactic Core: [VOID] Warp Core Cora holds the Galactic Core",
    ].join("\n"),
  );
});

test("gazette message copes with a quiet day", () => {
  assert.equal(
    message({ kind: "gazette", facts: { day: 1, headlines: [], wars: [], core: null } }),
    ["Day: 1", "Headlines:", "- none today", "Wars:", "- none"].join("\n"),
  );
});

test("recap message states the result and numbers", () => {
  assert.equal(
    message({ kind: "recap", facts: recap }),
    [
      "Attacker: [VOID] Warp Core Cora (120 ships, 30 lost)",
      "Defender: Dilithium Dylan (80 ships, 80 lost)",
      "Result: the attacker won after 5 rounds",
      "Clan support: [VOID]",
      "Plunder taken: 1,200 units of gold, quartz and helium",
      "Location: Kepler Reach",
    ].join("\n"),
  );
  const held = message({ kind: "recap", facts: { ...recap, winner: "defender", rounds: 1, loot: 0, clanSupport: null } });
  assert.match(held, /Result: the defender held after 1 round$/m);
  assert.doesNotMatch(held, /Plunder|Clan support/);
  assert.match(message({ kind: "recap", facts: { ...recap, winner: "draw", rounds: 0 } }), /Result: a draw$/m);
});

test("hail message turns personality numbers into words", () => {
  assert.equal(
    message({ kind: "hail", facts: hail }),
    [
      "Commander: Hubble Trouble",
      "Clan: [NOVA]",
      "Temperament: hot-headed, itching for a fight; more interested in glory than gold",
      "Grudge: yes, against Plasma Karen",
      "Strength: far stronger than Plasma Karen",
      "Hail from Plasma Karen: an offer of peace",
    ].join("\n"),
  );
  const calm = message({
    kind: "hail",
    facts: { ...hail, clan: null, aggression: 0.1, economyFocus: 0.9, grudge: false, mightRatio: 0.3, intent: "trade", playerName: "" },
  });
  assert.match(calm, /^Clan: none$/m);
  assert.match(calm, /^Temperament: calm and cautious; obsessed with profit$/m);
  assert.match(calm, /^Grudge: none$/m);
  assert.match(calm, /^Strength: far weaker than the player$/m);
  assert.match(calm, /^Hail from the player: a trade proposal$/m);
});

test("a seed adds a tone line, and the same seed always picks the same tone", () => {
  const withSeed = message({ kind: "recap", facts: recap, seed: 5 });
  assert.match(withSeed, /\nTone: \S.*$/);
  assert.equal(withSeed, message({ kind: "recap", facts: recap, seed: 5 }));
  assert.doesNotMatch(message({ kind: "recap", facts: recap }), /Tone:/);
  const tones = new Set([0, 1, 2, 3].map((seed) => toneFor("gazette", seed)));
  assert.equal(tones.size, 4);
  assert.equal(toneFor("hail", -1), toneFor("hail", 3), "negative seeds wrap around");
});

test("finishText strips emoji and markdown and uses ASCII punctuation", () => {
  const text = finishText("recap", "**Boom!** 🚀 Cora’s fleet — all 120 ships — hit “hard”… 👍🏽 Dylan’s 80 fell.");
  assert.equal(text, `Boom! Cora's fleet - all 120 ships - hit "hard"... Dylan's 80 fell.`);
  assert.doesNotMatch(text, /[^\x20-\x7e]/);
});

test("finishText keeps the gazette layout: headline, then one bullet per line", () => {
  const raw = "## VOID STRIKES AGAIN\n\n- Cora raided Dylan.\n* Karen held the line.\n•Nora sulks.\n\n• Core changes hands.";
  assert.equal(
    finishText("gazette", raw),
    "VOID STRIKES AGAIN\n• Cora raided Dylan.\n• Karen held the line.\n• Nora sulks.\n• Core changes hands.",
  );
});

test("finishText makes recaps and hails one paragraph and drops wrapping quotes", () => {
  assert.equal(finishText("recap", "Line one.\n\nLine two."), "Line one. Line two.");
  assert.equal(finishText("hail", '"Peace? Ha!"'), "Peace? Ha!");
  assert.equal(finishText("hail", '"Peace" is a big word. "Ha."'), '"Peace" is a big word. "Ha."');
  assert.equal(finishText("hail", "🚀✨"), "");
});

test("finishText never exceeds the kind's limit", () => {
  for (const kind of KINDS) {
    const long = Array.from({ length: 80 }, (_, i) => `Sentence ${i} is here.`).join(kind === "gazette" ? "\n• " : " ");
    assert.ok(finishText(kind, long).length <= MAX_CHARS[kind], kind);
  }
});

test("clampText leaves short text alone", () => {
  assert.equal(clampText("Short.", 10), "Short.");
});

test("clampText ends on a whole sentence or line when it can", () => {
  const text = "The first sentence ends here. The second one runs on and on past the limit";
  assert.equal(clampText(text, 40), "The first sentence ends here.");
  assert.equal(clampText("Headline\n• one item\n• another item that is long", 30), "Headline\n• one item");
  assert.equal(clampText("It ends right here. Next", 19), "It ends right here.");
});

test("clampText otherwise cuts at a word and adds an ellipsis", () => {
  const text = "no sentence ends anywhere in this rather long run of words";
  const out = clampText(text, 30);
  assert.equal(out, "no sentence ends anywhere...");
  assert.ok(out.length <= 30);
  assert.equal(clampText("x".repeat(50), 20), "x".repeat(17) + "...");
});
