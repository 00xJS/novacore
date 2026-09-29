// Battle replay (user request): watch a fight play out round by round — both
// fleets in formation as their ship art, bolts of fire, ships blowing up as
// they're lost, the counts ticking down — with 1× / 2× / 4× speed and SKIP.
// Everything comes from the report (BattleTimeline), so any battle in the
// mailbox can be replayed, and the replay always ends exactly where the
// report does. (Art upgrade, 2026-09-29: the rendered ships fly in and hold
// formation, small craft up front and capital ships behind; glowing bolts,
// muzzle flashes, layered explosions and wrecks; a nebula backdrop.)
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using Position = UnityEngine.UIElements.Position; // not the sim's map Position

namespace GalaxyRoyale.Game.UI
{
    public static class BattleReplayPanel
    {
        internal const string SpeedKey = "galaxyroyale.replay_speed";
        /// <summary>Icons per side — big fleets get one icon per group of ships.</summary>
        const int MaxIcons = 30;
        internal const float IntroSec = 1f;
        const float OutroSec = 0.35f;

        /// <summary>Anything to play back? Aegis deflections and unopposed raids have no rounds.</summary>
        public static bool CanReplay(BattleMailReport mail) => mail.Report.Rounds.Count > 0;

        /// <summary>Play <paramref name="mail"/>'s battle. <paramref name="onDone"/> runs on
        /// DONE / × (default: close the panel).</summary>
        public static void Open(GameContext ctx, BattleMailReport mail, Action? onDone = null)
        {
            var ui = UIController.Instance!;
            mail.Read = true;
            Action done = onDone ?? ui.CloseModal;

            var timeline = BattleTimeline.From(mail.Report);
            bool youAttacked = !mail.Defending;
            var yours = youAttacked ? timeline.Attacker : timeline.Defender;
            var theirs = youAttacked ? timeline.Defender : timeline.Attacker;
            int rounds = timeline.Rounds;
            // ~2.8 s a round at 1× so the losses can be followed; long fights
            // (10 rounds max) play a little faster per round.
            float roundSec = Mathf.Clamp(12f / Math.Max(1, rounds), 1.6f, 2.8f);
            float endAt = IntroSec + rounds * roundSec + OutroSec;

            var (blocker, content, footer) = Widgets.ModalPanelFooter("BATTLE REPLAY", done, 80f);

            // ---- header: them vs you ----
            var theirHead = SideHeader(MailboxPanel.EnemyNameOf(ctx, mail), UiTheme.Bad, out var theirCount);
            content.Add(theirHead);
            var (theirBar, theirFill) = Bar(UiTheme.Bad);
            content.Add(theirBar);

            var theirIcons = AllocateIcons(theirs[0], out var theirPerHull);
            var yourIcons = AllocateIcons(yours[0], out var yourPerHull);
            // Orbital Batteries sit with the defender: them when you attacked, you when raided.
            // Planetary guns (Orbital Batteries, Bastion railguns) sit with the defender.
            int battery = mail.Report.DefenderBattery + (mail.Report.DefenderTurret > 0 ? 1 : 0);
            var stage = new ReplayStage(theirIcons, yourIcons, mail.Id,
                batteries: battery > 0 ? 3 : 0, batteriesAreTheirs: youAttacked);
            stage.style.height = 330;
            stage.style.marginTop = 6;
            stage.style.marginBottom = 6;
            content.Add(stage);

            var (yourBar, yourFill) = Bar(UiTheme.Accent);
            content.Add(yourBar);
            var yourHead = SideHeader("YOU", UiTheme.Accent, out var yourCount);
            content.Add(yourHead);

            // The round (and at the end the outcome) shows mid-field, over the fight.
            var outcomeNote = Widgets.Text("", 11, UiTheme.Dim);
            outcomeNote.style.unityTextAlign = TextAnchor.MiddleCenter;
            outcomeNote.style.whiteSpace = WhiteSpace.Normal;
            outcomeNote.style.marginTop = 6;
            outcomeNote.style.display = DisplayStyle.None;
            content.Add(outcomeNote);

            // ---- per-hull tallies (them | you) ----
            var tallies = Widgets.HBox(Justify.SpaceBetween, Align.FlexStart);
            tallies.style.marginTop = 10;
            var r0 = mail.Report;
            var theirTally = Tally("THEIR FLEET", theirs[0], UiTheme.Bad, enemy: true,
                youAttacked ? r0.DefenderBattery : 0, youAttacked ? r0.DefenderTurret : 0);
            var yourTally = Tally("YOUR FLEET", yours[0], UiTheme.Accent, enemy: false,
                youAttacked ? 0 : r0.DefenderBattery, youAttacked ? 0 : r0.DefenderTurret);
            tallies.Add(theirTally.root);
            tallies.Add(yourTally.root);
            content.Add(tallies);

            // ---- the plan: who dies when, who shoots whom (seeded — replays match) ----
            var rng = new System.Random(mail.Id * 7919 + rounds);
            var deaths = new List<(ReplayStage.Ship ship, float at)>();
            PlanDeaths(stage.Theirs, theirs, theirPerHull, rounds, roundSec, rng, deaths);
            PlanDeaths(stage.Yours, yours, yourPerHull, rounds, roundSec, rng, deaths);
            PlanFire(stage, deaths, rounds, roundSec, rng);
            var applied = new bool[deaths.Count];

            // ---- playback ----
            int speed = PlayerPrefs.GetInt(SpeedKey, 1);
            if (speed != 2 && speed != 4) speed = 1;
            float now = 0f;
            bool finished = false;
            float lastTick = -1f;
            IVisualElementScheduledItem? loop = null;
            var speedButtons = new Dictionary<int, Button>();

            void Render()
            {
                for (int i = 0; i < deaths.Count; i++)
                {
                    if (applied[i] || deaths[i].at > now) continue;
                    applied[i] = true;
                    stage.Kill(deaths[i].ship, deaths[i].at, blast: !finished);
                }
                float their = CountAt(theirs, now), your = CountAt(yours, now);
                SetCount(theirCount, their);
                SetCount(yourCount, your);
                theirFill.style.width = Length.Percent(Pct(their, theirs[0]));
                yourFill.style.width = Length.Percent(Pct(your, yours[0]));
                theirTally.update(h => HullCountAt(theirs, h, now));
                yourTally.update(h => HullCountAt(yours, h, now));
                if (!finished)
                {
                    int r = now < IntroSec ? 0 : Math.Min(rounds, 1 + (int)((now - IntroSec) / roundSec));
                    stage.SetBanner(r == 0 ? "FLEETS ENGAGING" : $"ROUND {r} OF {rounds}", UiTheme.Magenta, 11);
                }
                stage.SetTime(now, sound: !finished);
            }

            float CountAt(List<Dictionary<HullId, int>> snaps, float t)
            {
                float total = 0f;
                foreach (var hull in snaps[0].Keys) total += HullCountAt(snaps, hull, t);
                return total;
            }

            // Counts fall during each round's casualty window, in step with the blasts.
            float HullCountAt(List<Dictionary<HullId, int>> snaps, HullId hull, float t)
            {
                int At(int i) => snaps[i].TryGetValue(hull, out var n) ? n : 0;
                if (t <= IntroSec) return At(0);
                float x = (t - IntroSec) / roundSec;
                int r = (int)x;
                if (r >= rounds) return At(rounds);
                float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.8f, x - r));
                return Mathf.Lerp(At(r), At(r + 1), k);
            }

            void Finish()
            {
                finished = true;
                now = endAt;
                loop?.Pause();
                Render();
                stage.SetTime(1e6f, sound: false); // clear tracers and blasts

                var r = mail.Report;
                bool draw = r.Winner == BattleWinner.Draw;
                bool won = mail.Defending ? r.Winner == BattleWinner.Defender : r.Winner == BattleWinner.Attacker;
                if (won) GameAudio.Feedback(Sfx.Victory, Haptic.Success);
                else GameAudio.Feedback(Sfx.Defeat, draw ? Haptic.Warning : Haptic.Error);
                stage.SetBanner(draw ? "STALEMATE" : won ? "VICTORY" : "DEFEAT",
                    draw ? UiTheme.Energy : won ? UiTheme.Good : UiTheme.Bad, 24);
                int yourLost = BattleTimeline.Count(yours[0]) - BattleTimeline.Count(yours[rounds]);
                int theirLost = BattleTimeline.Count(theirs[0]) - BattleTimeline.Count(theirs[rounds]);
                string note = $"{(rounds == 1 ? "1 round" : $"{rounds} rounds")} · you lost {yourLost:N0} · they lost {theirLost:N0}";
                if (r.Loot is { Total: > 0 } loot)
                    note += (mail.Defending ? " · plundered " : " · loot ") + UiTheme.FmtAmount(loot.Total);
                outcomeNote.text = note;
                outcomeNote.style.display = DisplayStyle.Flex;

                footer.Clear();
                var end = Widgets.HBox(Justify.SpaceBetween);
                var again = Widgets.IconButton(Icon.Rotate, "REPLAY", () => Open(ctx, mail, onDone), 11);
                again.style.width = Length.Percent(31f);
                var report = Widgets.TextButton("REPORT", () => MailboxPanel.OpenReport(ctx, mail), 11);
                report.style.width = Length.Percent(31f);
                var close = Widgets.TextButton("DONE", done, 11);
                close.style.width = Length.Percent(31f);
                end.Add(again);
                end.Add(report);
                end.Add(close);
                footer.Add(end);
            }

            void Tick()
            {
                if (finished) return;
                float t = Time.unscaledTime;
                if (!stage.LaidOut) { lastTick = t; return; } // clock starts once the icons are placed
                float dt = lastTick < 0f ? 0f : Mathf.Min(0.1f, t - lastTick);
                lastTick = t;
                now += dt * speed;
                ui.KeepSmooth(); // 60 fps while the battle plays
                if (now >= endAt) Finish();
                else Render();
            }

            // ---- footer: speed + skip ----
            var controls = Widgets.HBox(Justify.SpaceBetween);
            var speeds = Widgets.HBox();
            foreach (int s in new[] { 1, 2, 4 })
            {
                int value = s;
                var b = Widgets.TextButton($"{value}×", () =>
                {
                    speed = value;
                    PlayerPrefs.SetInt(SpeedKey, value);
                    foreach (var kv in speedButtons) Widgets.SetButtonHighlight(kv.Value, kv.Key == value);
                }, 12);
                b.style.width = 50;
                b.style.marginRight = 6;
                speedButtons[value] = b;
                Widgets.SetButtonHighlight(b, value == speed);
                speeds.Add(b);
            }
            controls.Add(speeds);
            var skip = Widgets.IconButton(Icon.Skip, "SKIP", Finish, 12);
            skip.style.width = 110;
            controls.Add(skip);
            footer.Add(controls);

            Render();
            loop = stage.schedule.Execute(Tick).Every(15);
            stage.RegisterCallback<DetachFromPanelEvent>(_ => loop?.Pause());
            ui.OpenModal(blocker);
        }

        // ---------- plan ----------

        /// <summary>One icon per ship for small fleets; otherwise ~MaxIcons split by
        /// hull share (every hull present keeps at least one), in Ships.All order.</summary>
        static List<HullId> AllocateIcons(Dictionary<HullId, int> fleet, out Dictionary<HullId, int> perHull)
        {
            perHull = new Dictionary<HullId, int>();
            int total = BattleTimeline.Count(fleet);
            foreach (var hull in Ships.All)
            {
                if (!fleet.TryGetValue(hull, out var n) || n <= 0) continue;
                perHull[hull] = total <= MaxIcons ? n
                    : Math.Max(1, (int)Math.Round(n * MaxIcons / (double)total));
            }
            int sum = 0;
            foreach (var v in perHull.Values) sum += v;
            while (sum > MaxIcons)
            {
                HullId biggest = default;
                int most = 1;
                foreach (var kv in perHull)
                    if (kv.Value > most) { most = kv.Value; biggest = kv.Key; }
                if (most <= 1) break;
                perHull[biggest]--;
                sum--;
            }
            var icons = new List<HullId>();
            foreach (var hull in Ships.All)
                if (perHull.TryGetValue(hull, out var k))
                    for (int i = 0; i < k; i++) icons.Add(hull);
            return icons;
        }

        /// <summary>Icons still standing for a hull: at least one while any ship of it lives.</summary>
        static int AliveIcons(Dictionary<HullId, int> start, Dictionary<HullId, int> snap,
            Dictionary<HullId, int> perHull, HullId hull)
        {
            int alive = snap.TryGetValue(hull, out var a) ? a : 0;
            if (alive <= 0) return 0;
            int from = start[hull];
            return Math.Max(1, (int)Math.Ceiling(alive * perHull[hull] / (double)from));
        }

        static void PlanDeaths(List<ReplayStage.Ship> ships, List<Dictionary<HullId, int>> snaps,
            Dictionary<HullId, int> perHull, int rounds, float roundSec, System.Random rng,
            List<(ReplayStage.Ship ship, float at)> deaths)
        {
            foreach (var hull in perHull.Keys)
            {
                var group = ships.FindAll(s => s.Hull == hull);
                for (int r = 1; r <= rounds; r++)
                {
                    int before = AliveIcons(snaps[0], snaps[r - 1], perHull, hull);
                    int after = AliveIcons(snaps[0], snaps[r], perHull, hull);
                    float roundStart = IntroSec + (r - 1) * roundSec;
                    for (int k = after; k < before && k < group.Count; k++)
                        deaths.Add((group[k], roundStart + roundSec * (0.28f + 0.45f * (float)rng.NextDouble())));
                }
            }
        }

        /// <summary>Every death gets the shot that caused it (from the other side,
        /// landing on the blast), plus volleys of fire in both directions.</summary>
        static void PlanFire(ReplayStage stage, List<(ReplayStage.Ship ship, float at)> deaths,
            int rounds, float roundSec, System.Random rng)
        {
            const float travel = 0.26f;
            var deadAt = new Dictionary<ReplayStage.Ship, float>();
            foreach (var d in deaths) deadAt[d.ship] = d.at;
            bool AliveAt(ReplayStage.Ship s, float t) => !deadAt.TryGetValue(s, out var at) || at > t;
            ReplayStage.Ship? Pick(List<ReplayStage.Ship> side, float t)
            {
                var alive = side.FindAll(s => AliveAt(s, t));
                return alive.Count == 0 ? null : alive[rng.Next(alive.Count)];
            }

            foreach (var d in deaths)
            {
                var shooters = d.ship.Enemy ? stage.Yours : stage.Theirs;
                var shooter = Pick(shooters, d.at - travel);
                // Raiders dying under the batteries: often the planetary guns' kill,
                // always when no defending ship is left to take the shot.
                bool underBatteries = stage.Batteries.Count > 0 && d.ship.Enemy != stage.BatteriesAreTheirs;
                if (underBatteries && (shooter == null || rng.NextDouble() < 0.45))
                    shooter = stage.Batteries[rng.Next(stage.Batteries.Count)];
                if (shooter != null) stage.AddTracer(shooter, d.ship, d.at - travel, travel);
            }
            for (int r = 1; r <= rounds; r++)
            {
                float roundStart = IntroSec + (r - 1) * roundSec;
                int alive = stage.Theirs.FindAll(s => AliveAt(s, roundStart)).Count
                          + stage.Yours.FindAll(s => AliveAt(s, roundStart)).Count;
                int volleys = Mathf.Clamp(alive / 2, 4, 16);
                for (int i = 0; i < volleys; i++)
                {
                    bool fromYou = i % 2 == 0;
                    float t0 = roundStart + roundSec * (0.04f + 0.6f * (float)rng.NextDouble());
                    var shooter = Pick(fromYou ? stage.Yours : stage.Theirs, t0);
                    var target = Pick(fromYou ? stage.Theirs : stage.Yours, t0 + travel);
                    if (shooter != null && target != null) stage.AddTracer(shooter, target, t0, travel);
                }
                // Battery salvos at the raiders.
                var raiders = stage.BatteriesAreTheirs ? stage.Yours : stage.Theirs;
                for (int i = 0; i < stage.Batteries.Count; i++)
                {
                    float t0 = roundStart + roundSec * (0.1f + 0.5f * (float)rng.NextDouble());
                    var target = Pick(raiders, t0 + travel);
                    if (target != null) stage.AddTracer(stage.Batteries[i], target, t0, travel);
                }
            }
        }

        // ---------- widgets ----------

        static VisualElement SideHeader(string name, Color color, out Label count)
        {
            var row = Widgets.HBox(Justify.SpaceBetween);
            row.style.marginTop = 4;
            var label = Widgets.Text(name, 13, color, bold: true);
            label.style.flexShrink = 1f;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(label);
            var right = Widgets.HBox();
            right.style.flexShrink = 0f;
            count = Widgets.Text("0", 18, color, bold: true);
            right.Add(count);
            var unit = Widgets.Text(" ships", 11, UiTheme.Dim);
            unit.style.marginLeft = 3;
            right.Add(unit);
            row.Add(right);
            return row;
        }

        static (VisualElement bar, VisualElement fill) Bar(Color color)
        {
            var bar = new VisualElement();
            bar.style.height = 5;
            bar.style.marginTop = 3;
            bar.style.backgroundColor = UiTheme.PanelLight;
            var fill = new VisualElement();
            fill.style.height = Length.Percent(100f);
            fill.style.width = Length.Percent(100f);
            fill.style.backgroundColor = color;
            bar.Add(fill);
            return (bar, fill);
        }

        static void SetCount(Label label, float value)
        {
            string text = Mathf.RoundToInt(value).ToString("N0");
            if (label.text != text) label.text = text;
        }

        static float Pct(float value, Dictionary<HullId, int> start)
        {
            int total = BattleTimeline.Count(start);
            return total <= 0 ? 0f : Mathf.Clamp(value / total * 100f, 0f, 100f);
        }

        static (VisualElement root, Action<Func<HullId, float>> update) Tally(
            string title, Dictionary<HullId, int> start, Color color, bool enemy, int battery, int turret)
        {
            var col = new VisualElement();
            col.style.width = Length.Percent(48f);
            col.Add(Widgets.Heading(title, 9, color, 2f));
            void Guns(string text)
            {
                var guns = Widgets.IconText(Icon.Target, text, 10, UiTheme.Good);
                guns.style.marginTop = 3;
                col.Add(guns);
            }
            if (battery > 0) Guns($"Orbital Batteries Lv {battery}");
            if (turret > 0) Guns($"Bastion railguns {turret:N0}");
            var labels = new List<(HullId hull, Label label)>();
            foreach (var hull in Ships.All)
            {
                if (!start.TryGetValue(hull, out var n) || n <= 0) continue;
                var row = Widgets.HBox(Justify.SpaceBetween);
                row.style.marginTop = 3;
                var name = Widgets.HBox();
                name.style.flexShrink = 1f;
                name.Add(ShipArt.Sprite(hull, 20f, enemy));
                var label = Widgets.Text(Ships.Defs[hull].Name, 11, UiTheme.Dim);
                label.style.marginLeft = 4;
                label.style.flexShrink = 1f;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                name.Add(label);
                row.Add(name);
                var count = Widgets.Text($"{n:N0} / {n:N0}", 11, UiTheme.Text, bold: true);
                count.style.flexShrink = 0f;
                row.Add(count);
                col.Add(row);
                labels.Add((hull, count));
            }
            return (col, countOf =>
            {
                foreach (var (hull, label) in labels)
                {
                    int alive = Mathf.RoundToInt(countOf(hull));
                    string text = $"{alive:N0} / {start[hull]:N0}";
                    if (label.text == text) continue;
                    label.text = text;
                    label.style.color = alive == 0 ? UiTheme.Bad : UiTheme.Text;
                }
            });
        }
    }

    /// <summary>The battlefield: both fleets in formation as their ship art (theirs
    /// on top, nose down; yours below, nose up; small craft up front and capital
    /// ships at the back) over a nebula, with an overlay painting the bolts,
    /// muzzle flashes and explosions.</summary>
    sealed class ReplayStage : VisualElement
    {
        public sealed class Ship
        {
            public HullId Hull;
            public bool Enemy;
            public bool IsBattery;
            public VisualElement Icon = null!;
            public Vector2 Center;
            public float Size = 20f;
            /// <summary>Its own beat for the idle drift and the tilt of its wreck.</summary>
            public float Phase;
            public bool Dead;

            /// <summary>Where its shots leave from: the nose, facing the enemy.</summary>
            public Vector2 Muzzle => IsBattery ? Center : Center + new Vector2(0f, (Enemy ? 0.36f : -0.36f) * Size);
        }

        /// <summary>The defender's Orbital Battery emplacements (never destroyed).</summary>
        public readonly List<Ship> Batteries = new();
        /// <summary>True when the batteries are the enemy's (you attacked their colony).</summary>
        public readonly bool BatteriesAreTheirs;

        readonly struct Tracer
        {
            public readonly Ship From, To;
            public readonly float T0, Dur;
            public Tracer(Ship from, Ship to, float t0, float dur) { From = from; To = to; T0 = t0; Dur = dur; }
        }

        readonly struct Blast
        {
            public readonly Vector2 At;
            public readonly float Size, T0, Spin;
            public Blast(Vector2 at, float size, float t0, float spin) { At = at; Size = size; T0 = t0; Spin = spin; }
        }

        /// <summary>An explosion's soft fireball and white-hot heart: glows that swell
        /// and fade (the shock ring and sparks are painted on the overlay).</summary>
        sealed class Fireball
        {
            public VisualElement Outer = null!, Inner = null!;
            public float T0;
        }

        const float Cut = 14f;
        const float BlastSec = 0.75f;
        static readonly Color YourFire = UiTheme.Accent;
        static readonly Color TheirFire = UiTheme.Magenta;
        static readonly Color GunFire = UiTheme.Energy;
        static readonly Color Flash = new(1f, 0.93f, 0.6f);
        static readonly Color Fire = new(1f, 0.6f, 0.22f);
        static readonly Color Wreck = new(0.34f, 0.31f, 0.38f, 0.8f);

        public readonly List<Ship> Theirs = new();
        public readonly List<Ship> Yours = new();
        readonly VisualElement _backdrop = new();
        readonly VisualElement _overlay = new();
        readonly Label _banner;
        float _bannerPx = 12f;
        readonly List<Tracer> _tracers = new();
        readonly List<Blast> _blasts = new();
        readonly List<Fireball> _fireballs = new();
        readonly List<Vector3> _stars = new();
        float _now;

        public bool LaidOut { get; private set; }

        public ReplayStage(List<HullId> theirs, List<HullId> yours, int seed,
            int batteries = 0, bool batteriesAreTheirs = true)
        {
            BatteriesAreTheirs = batteriesAreTheirs;
            Holo.Frame(this, UiTheme.Bg, Color.clear, Cut, 1.2f, FrameShape.BevelAll);
            style.overflow = Overflow.Hidden;
            pickingMode = PickingMode.Ignore;

            var rng = new System.Random(seed);
            for (int i = 0; i < 60; i++)
                _stars.Add(new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()));

            // Nebula: a violet glow across the middle, magenta over their side and
            // warmer toward yours; the stars and the line across the field on top.
            Add(Holo.Glow(new Color(0.42f, 0.16f, 0.72f, 0.6f), -8f, 12f, 96f, 72f));
            Add(Holo.Glow(new Color(1f, 0.24f, 0.85f, 0.32f), 40f, 4f, 62f, 44f));
            Add(Holo.Glow(new Color(1f, 0.6f, 0.24f, 0.26f), 0f, 54f, 62f, 44f));
            Stretch(_backdrop);
            _backdrop.generateVisualContent += PaintBackdrop;
            Add(_backdrop);

            foreach (var hull in theirs) Theirs.Add(MakeShip(hull, enemy: true, rng));
            foreach (var hull in yours) Yours.Add(MakeShip(hull, enemy: false, rng));
            for (int i = 0; i < batteries; i++)
            {
                var gun = Icons.Make(Icon.Target, 20f, UiTheme.Good);
                gun.style.position = Position.Absolute;
                Add(gun);
                Batteries.Add(new Ship { Hull = HullId.Probe, Enemy = batteriesAreTheirs, IsBattery = true, Icon = gun, Size = 20f });
            }

            _banner = Widgets.Heading("", 11, UiTheme.Magenta, 4f);
            _banner.pickingMode = PickingMode.Ignore;
            _banner.style.position = Position.Absolute;
            _banner.style.left = 0;
            _banner.style.right = 0;
            _banner.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(_banner);

            Stretch(_overlay);
            _overlay.generateVisualContent += Paint;
            Add(_overlay);

            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        static void Stretch(VisualElement e)
        {
            e.pickingMode = PickingMode.Ignore;
            e.style.position = Position.Absolute;
            e.style.left = 0;
            e.style.top = 0;
            e.style.right = 0;
            e.style.bottom = 0;
        }

        Ship MakeShip(HullId hull, bool enemy, System.Random rng)
        {
            var icon = ShipArt.Sprite(hull, 0f, enemy);
            icon.style.position = Position.Absolute;
            Add(icon);
            return new Ship { Hull = hull, Enemy = enemy, Icon = icon, Phase = (float)rng.NextDouble() * Mathf.PI * 2f };
        }

        /// <summary>The round mid-field while the fleets fight; the outcome at the end.</summary>
        public void SetBanner(string text, Color color, int size)
        {
            if (_banner.text == text) return;
            _banner.text = text;
            _banner.style.color = color;
            _bannerPx = Widgets.Sized(size * 0.86f);
            _banner.style.fontSize = _bannerPx;
            _banner.style.textShadow = new TextShadow
            {
                offset = Vector2.zero,
                blurRadius = size > 14 ? 14f : 8f,
                color = new Color(color.r, color.g, color.b, 0.9f),
            };
            PlaceBanner();
        }

        void PlaceBanner()
        {
            var r = contentRect;
            if (float.IsNaN(r.height) || r.height < 20f) return;
            float h = _bannerPx * 1.6f;
            _banner.style.top = r.y + r.height * 0.5f - h * 0.5f;
            _banner.style.height = h;
        }

        void Layout()
        {
            var r = contentRect;
            if (r.width < 20f || r.height < 20f) return;
            Place(Theirs, r, top: true);
            Place(Yours, r, top: false);
            // Batteries stand in front of the defenders, in the gap between fleets.
            for (int i = 0; i < Batteries.Count; i++)
            {
                float x = r.x + r.width * (0.22f + 0.28f * i) - 10f;
                float y = BatteriesAreTheirs ? r.y + r.height * 0.42f - 10f : r.y + r.height * 0.58f - 10f;
                Batteries[i].Icon.style.left = x;
                Batteries[i].Icon.style.top = y;
                Batteries[i].Center = new Vector2(x + 10f, y + 10f);
            }
            PlaceBanner();
            LaidOut = true;
            _backdrop.MarkDirtyRepaint();
            _overlay.MarkDirtyRepaint();
        }

        /// <summary>Rows of ships at the biggest size that fits the side's band: small
        /// craft in the front rows, capital ships in the back row by the edge.</summary>
        static void Place(List<Ship> ships, Rect r, bool top)
        {
            if (ships.Count == 0) return;
            var order = new List<Ship>(ships);
            var index = new Dictionary<Ship, int>();
            for (int i = 0; i < ships.Count; i++) index[ships[i]] = i;
            order.Sort((a, b) =>
            {
                int c = ShipArt.Bulk(a.Hull).CompareTo(ShipArt.Bulk(b.Hull));
                return c != 0 ? c : index[a].CompareTo(index[b]);
            });

            const float gap = 2f;
            float band = r.height * 0.4f - 8f;
            float width = r.width - 16f;
            List<List<Ship>> rows = new();
            float unit = 40f;
            for (; unit > 6f; unit -= 1f)
            {
                rows = Rows(order, unit, width, gap);
                float h = -gap;
                foreach (var row in rows) h += RowHeight(row, unit) + gap;
                if (h <= band) break;
            }

            // Back row first, against the edge; the front row ends nearest the enemy.
            float y = top ? r.y + 8f : r.yMax - 8f;
            for (int k = rows.Count - 1; k >= 0; k--)
            {
                var row = rows[k];
                float rh = RowHeight(row, unit);
                float rowWidth = -gap;
                foreach (var s in row) rowWidth += unit * ShipArt.Bulk(s.Hull) + gap;
                float x = r.x + (r.width - rowWidth) * 0.5f;
                float cy = top ? y + rh * 0.5f : y - rh * 0.5f;
                foreach (var s in row)
                {
                    float size = unit * ShipArt.Bulk(s.Hull);
                    s.Size = size;
                    s.Icon.style.left = x;
                    s.Icon.style.top = cy - size * 0.5f;
                    s.Icon.style.width = size;
                    s.Icon.style.height = size;
                    s.Center = new Vector2(x + size * 0.5f, cy);
                    x += size + gap;
                }
                y += top ? rh + gap : -(rh + gap);
            }
        }

        static List<List<Ship>> Rows(List<Ship> order, float unit, float width, float gap)
        {
            var rows = new List<List<Ship>>();
            var row = new List<Ship>();
            float used = 0f;
            foreach (var s in order)
            {
                float size = unit * ShipArt.Bulk(s.Hull);
                if (row.Count > 0 && used + gap + size > width)
                {
                    rows.Add(row);
                    row = new List<Ship>();
                    used = 0f;
                }
                used += (row.Count > 0 ? gap : 0f) + size;
                row.Add(s);
            }
            if (row.Count > 0) rows.Add(row);
            return rows;
        }

        static float RowHeight(List<Ship> row, float unit)
        {
            float h = 0f;
            foreach (var s in row) h = Mathf.Max(h, unit * ShipArt.Bulk(s.Hull));
            return h;
        }

        public void AddTracer(Ship from, Ship to, float t0, float dur) => _tracers.Add(new Tracer(from, to, t0, dur));

        /// <summary>A ship is lost: it goes up in a blast and leaves a dark wreck,
        /// knocked askew (a wreck, not a gap).</summary>
        public void Kill(Ship ship, float at, bool blast)
        {
            ship.Dead = true;
            var icon = ship.Icon;
            icon.style.translate = new Translate(0f, 0f);
            icon.style.unityBackgroundImageTintColor = Wreck;
            icon.style.opacity = ShipArt.Photo(ship.Hull) != null ? 0.8f : 0.2f;
            float tilt = (ship.Enemy ? 180f : 0f) + (ship.Phase > Mathf.PI ? 16f : -16f);
            icon.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(tilt)));
            icon.style.scale = new StyleScale(new Scale(new Vector3(0.82f, 0.82f, 1f)));
            if (!blast) return;
            _blasts.Add(new Blast(ship.Center, ship.Size, at, ship.Phase));
            float s = Mathf.Max(16f, ship.Size);
            var fireball = new Fireball
            {
                Outer = Burst(new Color(Fire.r, Fire.g, Fire.b, 0.95f), ship.Center, s * 2.3f),
                Inner = Burst(new Color(Flash.r, Flash.g, Flash.b, 1f), ship.Center, s * 1.1f),
                T0 = at,
            };
            _fireballs.Add(fireball);
            GameAudio.Play(Sfx.Explosion, 1f, UnityEngine.Random.Range(0.85f, 1.15f));
            GameAudio.Buzz(Haptic.Light);
        }

        /// <summary>A glow over the ships (under the banner and the overlay), hidden
        /// until SetTime lights it.</summary>
        VisualElement Burst(Color color, Vector2 at, float size)
        {
            var glow = Holo.Glow(color);
            glow.style.left = at.x - size * 0.5f;
            glow.style.top = at.y - size * 0.5f;
            glow.style.width = size;
            glow.style.height = size;
            glow.style.opacity = 0f;
            Insert(IndexOf(_banner), glow);
            return glow;
        }

        public void SetTime(float now, bool sound = true)
        {
            // A zap for each shot fired since the last frame (GameAudio throttles
            // bursts); the planetary guns boom lower.
            if (sound)
                foreach (var t in _tracers)
                    if (t.T0 > _now && t.T0 <= now)
                        GameAudio.Play(Sfx.Laser, 1f, t.From.IsBattery
                            ? 0.55f : UnityEngine.Random.Range(0.9f, 1.2f));
            _now = now;

            // The fleets fly in from their edges while they close, then hold
            // station with a gentle drift.
            float approach = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(now / BattleReplayPanel.IntroSec));
            foreach (var s in Theirs) Drift(s, now, approach);
            foreach (var s in Yours) Drift(s, now, approach);

            // Fireballs swell and fade; the heart burns out first.
            for (int i = _fireballs.Count - 1; i >= 0; i--)
            {
                var f = _fireballs[i];
                float k = (now - f.T0) / BlastSec;
                if (k >= 1f)
                {
                    f.Outer.RemoveFromHierarchy();
                    f.Inner.RemoveFromHierarchy();
                    _fireballs.RemoveAt(i);
                    continue;
                }
                k = Mathf.Max(0f, k);
                // Gone quickly: a faint orange glow over the violet nebula reads as a dull disc.
                f.Outer.style.opacity = 0.95f * (1f - k) * (1f - k) * (1f - k);
                f.Outer.style.scale = new StyleScale(new Scale(Vector3.one * (0.5f + 0.6f * k)));
                f.Inner.style.opacity = Mathf.Clamp01(1.2f - 2f * k);
                f.Inner.style.scale = new StyleScale(new Scale(Vector3.one * (1f - 0.5f * k)));
            }
            _overlay.MarkDirtyRepaint();
        }

        static void Drift(Ship s, float now, float approach)
        {
            if (s.Dead) return;
            float y = (s.Enemy ? -1f : 1f) * 90f * approach + Mathf.Sin(now * 1.7f + s.Phase) * 1.4f;
            s.Icon.style.translate = new Translate(0f, y);
        }

        // ---------- painting ----------

        void PaintBackdrop(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            if (r.width < 1f) return;

            foreach (var star in _stars)
            {
                float a = 0.1f + 0.45f * star.z * star.z;
                p.fillColor = new Color(1f, 1f, 1f, a);
                p.BeginPath();
                p.Arc(new Vector2(r.x + star.x * r.width, r.y + star.y * r.height), 0.5f + 1.1f * star.z,
                    Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Fill();
            }

            // No man's land: a faint dashed line across the middle, broken where
            // the round (and the outcome) shows.
            p.strokeColor = UiTheme.A(UiTheme.Accent, 0.16f);
            p.lineWidth = 1f;
            float mid = r.y + r.height * 0.5f;
            float gapFrom = r.x + r.width * 0.26f, gapTo = r.x + r.width * 0.74f;
            for (float x = r.x + 10f; x < r.xMax - 10f; x += 14f)
            {
                if (x + 7f > gapFrom && x < gapTo) continue;
                p.BeginPath();
                p.MoveTo(new Vector2(x, mid));
                p.LineTo(new Vector2(Mathf.Min(x + 7f, r.xMax - 10f), mid));
                p.Stroke();
            }
        }

        static void Disc(Painter2D p, Vector2 at, float radius, Color color)
        {
            if (radius <= 0.1f || color.a <= 0.005f) return;
            p.fillColor = color;
            p.BeginPath();
            p.Arc(at, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b, float width, Color color)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        void Paint(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            if (r.width < 1f) return;
            p.lineCap = LineCap.Round;

            // Bolts: a glowing streak flying from the shooter's nose to the
            // target, a muzzle flash as it leaves and a flash where it hits.
            foreach (var t in _tracers)
            {
                float k = (_now - t.T0) / t.Dur;
                if (k < 0f || k > 1.4f) continue;
                var color = t.From.IsBattery ? GunFire : t.From.Enemy ? TheirFire : YourFire;
                Vector2 a = t.From.Muzzle, b = t.To.Center;
                if (k < 0.3f)
                {
                    float f = k / 0.3f;
                    Disc(p, a, (t.From.IsBattery ? 9f : 6f) * (1f - 0.5f * f), new Color(Flash.r, Flash.g, Flash.b, 0.35f * (1f - f)));
                    Disc(p, a, (t.From.IsBattery ? 4f : 2.6f) * (1f - f) + 0.5f, new Color(1f, 1f, 1f, 1f - f));
                }
                if (k <= 1f)
                {
                    var head = Vector2.Lerp(a, b, k);
                    var tail = Vector2.Lerp(a, b, Mathf.Max(0f, k - (t.From.IsBattery ? 0.45f : 0.3f)));
                    float w = t.From.IsBattery ? 3.4f : 2.2f;
                    Line(p, tail, head, w + 5f, new Color(color.r, color.g, color.b, 0.18f));
                    Line(p, tail, head, w, color);
                    Disc(p, head, w * 0.8f, Color.white);
                }
                else
                {
                    float f = (k - 1f) / 0.4f;
                    Disc(p, b, 3f + 7f * f, new Color(color.r, color.g, color.b, 0.5f * (1f - f)));
                    Disc(p, b, 2.2f * (1f - f) + 0.5f, new Color(1f, 1f, 1f, 1f - f));
                }
            }

            // Explosions (over the fireball glows): a white-hot core, a shock ring
            // and sparks thrown clear, sized to the ship.
            foreach (var b in _blasts)
            {
                float k = (_now - b.T0) / BlastSec;
                if (k < 0f || k > 1f) continue;
                float s = Mathf.Max(14f, b.Size);
                float fade = 1f - k;
                Disc(p, b.At, s * 0.12f * fade + 0.5f, new Color(1f, 1f, 1f, fade));
                p.strokeColor = new Color(1f, 0.72f, 0.38f, 0.7f * fade);
                p.lineWidth = 1.4f;
                p.BeginPath();
                p.Arc(b.At, s * (0.25f + 0.65f * k), Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Stroke();
                for (int i = 0; i < 8; i++)
                {
                    float ang = b.Spin + i * Mathf.PI / 4f + (i % 2) * 0.3f;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float r0 = s * (0.2f + 0.75f * k), r1 = r0 + s * 0.16f * fade;
                    Line(p, b.At + dir * r0, b.At + dir * r1, 1.4f, new Color(1f, 0.85f, 0.45f, fade));
                }
            }

            // The frame on top of everything, cut-cornered like the rest of the hologram.
            float x0 = r.xMin + 0.6f, y0 = r.yMin + 0.6f, x1 = r.xMax - 0.6f, y1 = r.yMax - 0.6f;
            void Frame(float width, Color color)
            {
                p.strokeColor = color;
                p.lineWidth = width;
                p.lineJoin = LineJoin.Miter;
                p.BeginPath();
                p.MoveTo(new Vector2(x0 + Cut, y0));
                p.LineTo(new Vector2(x1 - Cut, y0));
                p.LineTo(new Vector2(x1, y0 + Cut));
                p.LineTo(new Vector2(x1, y1 - Cut));
                p.LineTo(new Vector2(x1 - Cut, y1));
                p.LineTo(new Vector2(x0 + Cut, y1));
                p.LineTo(new Vector2(x0, y1 - Cut));
                p.LineTo(new Vector2(x0, y0 + Cut));
                p.ClosePath();
                p.Stroke();
            }
            Frame(4f, UiTheme.A(UiTheme.Accent, 0.12f));
            Frame(1.2f, UiTheme.A(UiTheme.Accent, 0.7f));
        }
    }
}
