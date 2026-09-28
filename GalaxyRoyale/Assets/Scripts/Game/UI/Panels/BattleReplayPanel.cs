// Battle replay (user request): watch a fight play out round by round — both
// fleets as ship icons, tracer fire, ships blowing up as they're lost, the
// counts ticking down — with 1× / 2× / 4× speed and SKIP. Everything comes
// from the report (BattleTimeline), so any battle in the mailbox can be
// replayed, and the replay always ends exactly where the report does.
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
        const string SpeedKey = "galaxyroyale.replay_speed";
        /// <summary>Icons per side — big fleets get one icon per group of ships.</summary>
        const int MaxIcons = 30;
        const float IntroSec = 1f;
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
            int battery = mail.Report.DefenderBattery;
            var stage = new ReplayStage(theirIcons, yourIcons, mail.Id,
                batteries: battery > 0 ? 3 : 0, batteriesAreTheirs: youAttacked);
            stage.style.height = 280;
            stage.style.marginTop = 6;
            stage.style.marginBottom = 6;
            content.Add(stage);

            var (yourBar, yourFill) = Bar(UiTheme.Accent);
            content.Add(yourBar);
            var yourHead = SideHeader("YOU", UiTheme.Accent, out var yourCount);
            content.Add(yourHead);

            var roundLabel = Widgets.Text("FLEETS ENGAGING", 11, UiTheme.Dim, bold: true);
            roundLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            roundLabel.style.marginTop = 6;
            content.Add(roundLabel);

            var outcome = Widgets.Text("", 22, UiTheme.Text, bold: true);
            outcome.style.unityTextAlign = TextAnchor.MiddleCenter;
            outcome.style.display = DisplayStyle.None;
            content.Add(outcome);
            var outcomeNote = Widgets.Text("", 11, UiTheme.Dim);
            outcomeNote.style.unityTextAlign = TextAnchor.MiddleCenter;
            outcomeNote.style.whiteSpace = WhiteSpace.Normal;
            outcomeNote.style.display = DisplayStyle.None;
            content.Add(outcomeNote);

            // ---- per-hull tallies (them | you) ----
            var tallies = Widgets.HBox(Justify.SpaceBetween, Align.FlexStart);
            tallies.style.marginTop = 10;
            var theirTally = Tally("THEIR FLEET", theirs[0], UiTheme.Bad, youAttacked ? battery : 0);
            var yourTally = Tally("YOUR FLEET", yours[0], UiTheme.Accent, youAttacked ? 0 : battery);
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
                    roundLabel.text = r == 0 ? "FLEETS ENGAGING" : $"ROUND {r} / {rounds}";
                }
                stage.SetTime(now);
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
                stage.SetTime(1e6f); // clear tracers and blasts
                roundLabel.text = rounds == 1 ? "1 ROUND" : $"{rounds} ROUNDS";

                var r = mail.Report;
                bool draw = r.Winner == BattleWinner.Draw;
                bool won = mail.Defending ? r.Winner == BattleWinner.Defender : r.Winner == BattleWinner.Attacker;
                outcome.text = draw ? "STALEMATE" : won ? "VICTORY" : "DEFEAT";
                outcome.style.color = draw ? UiTheme.Energy : won ? UiTheme.Good : UiTheme.Bad;
                outcome.style.display = DisplayStyle.Flex;
                int yourLost = BattleTimeline.Count(yours[0]) - BattleTimeline.Count(yours[rounds]);
                int theirLost = BattleTimeline.Count(theirs[0]) - BattleTimeline.Count(theirs[rounds]);
                string note = $"You lost {yourLost:N0} · they lost {theirLost:N0}";
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
            string title, Dictionary<HullId, int> start, Color color, int battery)
        {
            var col = new VisualElement();
            col.style.width = Length.Percent(48f);
            col.Add(Widgets.Text(title, 9, color, bold: true));
            if (battery > 0)
            {
                var guns = Widgets.IconText(Icon.Target, $"Orbital Batteries Lv {battery}", 10, UiTheme.Good);
                guns.style.marginTop = 2;
                col.Add(guns);
            }
            var labels = new List<(HullId hull, Label label)>();
            foreach (var hull in Ships.All)
            {
                if (!start.TryGetValue(hull, out var n) || n <= 0) continue;
                var row = Widgets.HBox(Justify.SpaceBetween);
                row.style.marginTop = 2;
                row.Add(Widgets.Text(Ships.Defs[hull].Name, 11, UiTheme.Dim));
                var count = Widgets.Text($"{n:N0} / {n:N0}", 11, UiTheme.Text, bold: true);
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

    /// <summary>The battlefield: both fleets as ship icons (theirs on top, nose
    /// down; yours below, nose up) plus an overlay painting tracers and blasts.</summary>
    sealed class ReplayStage : VisualElement
    {
        public sealed class Ship
        {
            public HullId Hull;
            public bool Enemy;
            public VisualElement Icon = null!;
            public Vector2 Center;
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

        static readonly Color YourFire = new(0.5f, 0.85f, 1f);
        static readonly Color TheirFire = new(1f, 0.5f, 0.35f);
        static readonly Color BlastCore = new(1f, 0.78f, 0.35f);

        public readonly List<Ship> Theirs = new();
        public readonly List<Ship> Yours = new();
        readonly VisualElement _overlay = new();
        readonly List<Tracer> _tracers = new();
        readonly List<(Vector2 at, float t0)> _blasts = new();
        readonly List<Vector3> _stars = new();
        float _now;

        public bool LaidOut { get; private set; }

        public ReplayStage(List<HullId> theirs, List<HullId> yours, int seed,
            int batteries = 0, bool batteriesAreTheirs = true)
        {
            BatteriesAreTheirs = batteriesAreTheirs;
            style.backgroundColor = new Color(0.03f, 0.05f, 0.1f);
            Widgets.SetBorder(this, UiTheme.Stroke, 1f);
            style.borderTopLeftRadius = 8;
            style.borderTopRightRadius = 8;
            style.borderBottomLeftRadius = 8;
            style.borderBottomRightRadius = 8;
            style.overflow = Overflow.Hidden;
            pickingMode = PickingMode.Ignore;

            var rng = new System.Random(seed);
            for (int i = 0; i < 36; i++)
                _stars.Add(new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(),
                    0.5f + (float)rng.NextDouble()));

            foreach (var hull in theirs) Theirs.Add(MakeShip(hull, enemy: true));
            foreach (var hull in yours) Yours.Add(MakeShip(hull, enemy: false));
            for (int i = 0; i < batteries; i++)
            {
                var gun = Icons.Make(Icon.Target, 18f, UiTheme.Good);
                gun.style.position = Position.Absolute;
                Add(gun);
                Batteries.Add(new Ship { Hull = HullId.Probe, Enemy = batteriesAreTheirs, Icon = gun });
            }

            _overlay.pickingMode = PickingMode.Ignore;
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;
            _overlay.generateVisualContent += Paint;
            Add(_overlay);

            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        Ship MakeShip(HullId hull, bool enemy)
        {
            var icon = new ShipSchematic(hull) { pickingMode = PickingMode.Ignore };
            icon.style.position = Position.Absolute;
            icon.style.backgroundColor = enemy
                ? new Color(1f, 0.35f, 0.3f, 0.14f)
                : new Color(0.45f, 0.8f, 1f, 0.14f);
            if (enemy) icon.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(180f)));
            Add(icon);
            return new Ship { Hull = hull, Enemy = enemy, Icon = icon };
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
                float x = r.x + r.width * (0.22f + 0.28f * i) - 9f;
                float y = BatteriesAreTheirs ? r.y + r.height * 0.43f : r.y + r.height * 0.57f - 18f;
                Batteries[i].Icon.style.left = x;
                Batteries[i].Icon.style.top = y;
                Batteries[i].Center = new Vector2(x + 9f, y + 9f);
            }
            LaidOut = true;
            _overlay.MarkDirtyRepaint();
        }

        static void Place(List<Ship> ships, Rect r, bool top)
        {
            int n = ships.Count;
            if (n == 0) return;
            const float gap = 5f;
            int cols = Math.Min(10, n);
            int rows = (n + cols - 1) / cols;
            float band = r.height * 0.38f;
            float size = Mathf.Min(30f, (r.width - 20f - (cols - 1) * gap) / cols,
                (band - 10f - (rows - 1) * gap) / rows);
            for (int i = 0; i < n; i++)
            {
                int row = i / cols, col = i % cols;
                int inRow = Math.Min(cols, n - row * cols);
                float rowWidth = inRow * size + (inRow - 1) * gap;
                float x = r.x + (r.width - rowWidth) * 0.5f + col * (size + gap);
                float y = top
                    ? r.y + 10f + row * (size + gap)
                    : r.yMax - 10f - size - row * (size + gap);
                var icon = ships[i].Icon;
                icon.style.left = x;
                icon.style.top = y;
                icon.style.width = size;
                icon.style.height = size;
                icon.style.borderTopLeftRadius = size * 0.5f;
                icon.style.borderTopRightRadius = size * 0.5f;
                icon.style.borderBottomLeftRadius = size * 0.5f;
                icon.style.borderBottomRightRadius = size * 0.5f;
                ships[i].Center = new Vector2(x + size * 0.5f, y + size * 0.5f);
            }
        }

        public void AddTracer(Ship from, Ship to, float t0, float dur) => _tracers.Add(new Tracer(from, to, t0, dur));

        /// <summary>A ship is lost: its icon goes dark (a wreck, not a gap), with a blast.</summary>
        public void Kill(Ship ship, float at, bool blast)
        {
            ship.Icon.style.opacity = 0.14f;
            ship.Icon.style.scale = new StyleScale(new Scale(new Vector3(0.8f, 0.8f, 1f)));
            if (blast) _blasts.Add((ship.Center, at));
        }

        public void SetTime(float now)
        {
            _now = now;
            _overlay.MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            var r = contentRect;
            if (r.width < 1f) return;

            foreach (var star in _stars)
            {
                p.fillColor = new Color(1f, 1f, 1f, 0.12f + 0.2f * (star.z - 0.5f));
                p.BeginPath();
                p.Arc(new Vector2(r.x + star.x * r.width, r.y + star.y * r.height), star.z,
                    Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Fill();
            }

            // Tracers: a streak flying from shooter to target, then a hit flash.
            p.lineCap = LineCap.Round;
            foreach (var t in _tracers)
            {
                float k = (_now - t.T0) / t.Dur;
                if (k < 0f || k > 1.4f) continue;
                var color = t.From.Enemy ? TheirFire : YourFire;
                Vector2 a = t.From.Center, b = t.To.Center;
                if (k <= 1f)
                {
                    var head = Vector2.Lerp(a, b, k);
                    var tail = Vector2.Lerp(a, b, Mathf.Max(0f, k - 0.35f));
                    p.strokeColor = color;
                    p.lineWidth = 2f;
                    p.BeginPath();
                    p.MoveTo(tail);
                    p.LineTo(head);
                    p.Stroke();
                }
                else
                {
                    float f = (k - 1f) / 0.4f;
                    p.fillColor = new Color(color.r, color.g, color.b, 1f - f);
                    p.BeginPath();
                    p.Arc(b, 2f + 5f * f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.ClosePath();
                    p.Fill();
                }
            }

            // Blasts: a hot core collapsing inside an expanding shock ring.
            foreach (var (at, t0) in _blasts)
            {
                float k = (_now - t0) / 0.55f;
                if (k < 0f || k > 1f) continue;
                p.fillColor = new Color(BlastCore.r, BlastCore.g, BlastCore.b, 1f - 0.6f * k);
                p.BeginPath();
                p.Arc(at, Mathf.Lerp(11f, 1f, k), Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Fill();
                p.strokeColor = new Color(1f, 0.55f, 0.25f, 1f - k);
                p.lineWidth = 2f;
                p.BeginPath();
                p.Arc(at, Mathf.Lerp(5f, 24f, k), Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Stroke();
            }
        }
    }
}
