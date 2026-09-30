// The new-commander training's coach (2026-09-30): a card saying what to do next
// and a pulsing spotlight on exactly where to tap. It never blocks the game —
// every tap still reaches the button under it — and it follows the player: if
// they wander off (another panel, the map, the wrong district), the spotlight
// points the way back one tap at a time, ending on the step's own button.
// The steps and their rules live in Data/Tutorial + Sim/TutorialSystem; the
// controls it points at carry "tut-…" names.
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game.UI
{
    public sealed class TutorialCoach
    {
        readonly GameContext _ctx;
        readonly VisualElement _root, _layer, _card, _ring, _arrow, _pill;
        readonly Label _chapter, _count, _title, _text, _status;
        readonly Button _next, _skip, _hide;
        string _shownStep = "";
        bool _collapsed;
        VisualElement? _scrolledTo;

        /// <summary>A target on screen: a control, or a spot on the globe.</summary>
        readonly struct Hint
        {
            public readonly VisualElement? Element;
            public readonly Rect? Area;
            public readonly string? Status;

            public Hint(VisualElement? element, Rect? area = null, string? status = null)
            {
                Element = element; Area = area; Status = status;
            }

            public static Hint Say(string status) => new(null, null, status);
            public Hint With(string? status) => new(Element, Area, status ?? Status);
            public bool Points => Element != null || Area != null;
        }

        public TutorialCoach(VisualElement root, GameContext ctx)
        {
            _root = root;
            _ctx = ctx;
            _layer = new VisualElement { name = "tutorial-coach", pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0;
            _layer.style.right = 0;
            _layer.style.top = 0;
            _layer.style.bottom = 0;
            root.Add(_layer); // last child: over the HUD and over every panel

            // The spotlight: a glowing ring round the target and an arrow into it.
            _ring = new VisualElement { pickingMode = PickingMode.Ignore };
            _ring.style.position = Position.Absolute;
            Holo.Frame(_ring, Color.clear, UiTheme.Accent, 10f, 3f, FrameShape.BevelAll, glow: true);
            _layer.Add(_ring);
            _arrow = Icons.Make(Icon.ChevronDown, 30f, UiTheme.Accent);
            _arrow.pickingMode = PickingMode.Ignore;
            _arrow.style.position = Position.Absolute;
            _layer.Add(_arrow);

            _card = new VisualElement();
            _card.style.position = Position.Absolute;
            _card.style.left = 12;
            _card.style.right = 12;
            _card.style.paddingLeft = 14;
            _card.style.paddingRight = 14;
            _card.style.paddingTop = 11;
            _card.style.paddingBottom = 11;
            Holo.Frame(_card, UiTheme.A(UiTheme.Bg, 0.97f), UiTheme.Accent, 12f, 1.5f, FrameShape.Bevel, glow: true);

            var top = Widgets.HBox(Justify.SpaceBetween);
            _chapter = Widgets.Heading("", 8, UiTheme.Magenta, 1.8f);
            top.Add(_chapter);
            var right = Widgets.HBox();
            _count = Widgets.Heading("", 8, UiTheme.Dim, 1.2f);
            right.Add(_count);
            _hide = Widgets.TextButton("–", () => { _collapsed = true; Refresh(force: true); }, 12);
            _hide.style.width = 30;
            _hide.style.height = 24;
            _hide.style.marginLeft = 8;
            _hide.style.paddingLeft = 0;
            _hide.style.paddingRight = 0;
            right.Add(_hide);
            top.Add(right);
            _card.Add(top);

            _title = Widgets.Heading("", 14, UiTheme.Accent, 2f);
            _title.style.marginTop = 2;
            _title.style.whiteSpace = WhiteSpace.Normal;
            _card.Add(_title);
            _text = Widgets.Text("", 12, UiTheme.Text);
            _text.style.whiteSpace = WhiteSpace.Normal;
            _text.style.marginTop = 5;
            _card.Add(_text);
            _status = Widgets.Text("", 11, UiTheme.Energy, bold: true);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginTop = 6;
            _card.Add(_status);

            var buttons = Widgets.HBox(Justify.SpaceBetween);
            buttons.style.marginTop = 10;
            _skip = Widgets.TextButton("SKIP TRAINING", ConfirmSkip, 10);
            _skip.style.height = 34;
            _skip.style.width = Length.Percent(44f);
            buttons.Add(_skip);
            _next = Widgets.Primary(Widgets.TextButton("NEXT", OnNext, 11));
            _next.name = "tut-next";
            _next.style.height = 34;
            _next.style.width = Length.Percent(44f);
            buttons.Add(_next);
            _card.Add(buttons);
            _layer.Add(_card);

            // Collapsed: a small tab that brings the card back.
            _pill = new Button(() => { _collapsed = false; Refresh(force: true); }) { text = "" };
            _pill.style.position = Position.Absolute;
            _pill.style.right = 12;
            _pill.style.paddingLeft = 10;
            _pill.style.paddingRight = 10;
            _pill.style.height = 30;
            Holo.Frame(_pill, UiTheme.A(UiTheme.Bg, 0.95f), UiTheme.Accent, 7f, 1.2f, FrameShape.Bevel, glow: true);
            _pill.Add(Widgets.Heading("TRAINING ▸", 9, UiTheme.Accent, 1.4f));
            _layer.Add(_pill);

            _layer.schedule.Execute(() => Refresh(force: false)).Every(120);
            _layer.schedule.Execute(Pulse).Every(33);
            Hide();
        }

        void Hide()
        {
            _card.style.display = DisplayStyle.None;
            _pill.style.display = DisplayStyle.None;
            _ring.style.display = DisplayStyle.None;
            _arrow.style.display = DisplayStyle.None;
        }

        void OnNext()
        {
            var state = _ctx.State;
            if (state == null) return;
            bool last = state.TutorialStep == Tutorial.Steps.Count - 1;
            if (!TutorialSystem.Next(state).Ok) return;
            GameAudio.Feedback(last ? Sfx.Success : Sfx.Toggle, Haptic.Light);
            if (last)
            {
                UIController.Instance?.Toast(
                    $"Training complete: +{Tutorial.Reward.Gold:N0} gold, +{Tutorial.Reward.Quartz:N0} quartz, " +
                    $"+{Tutorial.Reward.Helium:N0} helium, +{Tutorial.RewardDarkMatter} Dark Matter", Icon.Trophy, UiTheme.Good);
                LocalBootstrap.RequestSync();
            }
            Refresh(force: true);
        }

        void ConfirmSkip()
        {
            ConfirmPanel.Open("Leave the training?\nYou can start it again from Settings, under Help.", "LEAVE TRAINING", () =>
            {
                if (_ctx.State != null) TutorialSystem.Skip(_ctx.State);
                LocalBootstrap.RequestSync();
                Refresh(force: true);
            });
        }

        // ---------- per frame ----------

        void Refresh(bool force)
        {
            var state = _ctx.State;
            var ui = UIController.Instance;
            if (state == null || ui == null || !LocalBootstrap.Booted || GlobeTour.Running) { Hide(); return; }
            if (TutorialSystem.Check(state)) { _collapsed = false; GameAudio.Play(Sfx.Quest); }
            var step = TutorialSystem.Current(state);
            if (step == null) { Hide(); return; }

            if (step.Id != _shownStep || force)
            {
                if (step.Id != _shownStep) { _collapsed = false; _scrolledTo = null; }
                _shownStep = step.Id;
                var (n, total) = TutorialSystem.Position(state);
                _chapter.text = step.Chapter;
                _count.text = $"{n} / {total}";
                _title.text = step.Title;
                _text.text = step.Text;
                bool reading = step.Goal == TutorialGoal.Next;
                _next.style.display = reading ? DisplayStyle.Flex : DisplayStyle.None;
                _next.text = step.Id == "done" ? "FINISH" : "NEXT";
                _skip.visible = step.Id != "done"; // nothing left to skip on the last card
            }

            var hint = HintFor(step, state, ui);
            _status.text = hint.Status ?? "";
            _status.style.display = string.IsNullOrEmpty(hint.Status) ? DisplayStyle.None : DisplayStyle.Flex;

            Rect? area = hint.Element != null ? hint.Element.worldBound : hint.Area;
            if (hint.Element != null) ScrollIntoView(hint.Element);
            PlaceCard(area);
            PlaceSpotlight(area);
        }

        void PlaceCard(Rect? target)
        {
            float h = _root.layout.height > 0 ? _root.layout.height : 844f;
            bool targetLow = target is { } t && t.center.y > h * 0.5f;
            if (_collapsed)
            {
                _card.style.display = DisplayStyle.None;
                _pill.style.display = DisplayStyle.Flex;
                _pill.style.top = targetLow ? Px(SafeTop() + 4f) : Auto;
                _pill.style.bottom = targetLow ? Auto : Px(UiTheme.NavH + 34f);
                return;
            }
            _pill.style.display = DisplayStyle.None;
            _card.style.display = DisplayStyle.Flex;
            // With a panel open the card shrinks to its title and status and docks over
            // the nav bar, so it never covers the panel's own buttons.
            bool compact = UIController.Instance?.CurrentModal != null
                && _ctx.State != null && TutorialSystem.Current(_ctx.State) is { Goal: not TutorialGoal.Next };
            _text.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
            _skip.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
            if (compact)
            {
                _card.style.top = Auto;
                _card.style.bottom = Px(SafeBottom() + 2f);
                return;
            }
            // Over the half of the screen the target isn't in: over the header when the
            // target is low, above the nav bar when it's high (or when there's none).
            _card.style.top = targetLow ? Px(SafeTop() + 4f) : Auto;
            _card.style.bottom = targetLow ? Auto : Px(UiTheme.NavH + 34f);
        }

        static readonly StyleLength Auto = new(StyleKeyword.Auto);
        static StyleLength Px(float v) => new(v);

        /// <summary>The home indicator's strip at the bottom, in panel points.</summary>
        float SafeBottom()
        {
            float h = _root.layout.height > 0 ? _root.layout.height : 844f;
            return Screen.height > 0 ? Screen.safeArea.yMin * h / Screen.height : 0f;
        }

        /// <summary>The top of the safe area (below the Dynamic Island), in panel points.</summary>
        float SafeTop()
        {
            float h = _root.layout.height > 0 ? _root.layout.height : 844f;
            float inset = Screen.height > 0 ? (Screen.height - Screen.safeArea.yMax) * h / Screen.height : 0f;
            return Mathf.Max(20f, inset);
        }

        void PlaceSpotlight(Rect? target)
        {
            if (target is not { } r || r.width < 2f)
            {
                _ring.style.display = DisplayStyle.None;
                _arrow.style.display = DisplayStyle.None;
                return;
            }
            const float pad = 6f;
            _ring.style.display = DisplayStyle.Flex;
            _ring.style.left = r.xMin - pad;
            _ring.style.top = r.yMin - pad;
            _ring.style.width = r.width + pad * 2f;
            _ring.style.height = r.height + pad * 2f;
            // The arrow comes from the card's side: above a low target, below a high one.
            float h = _root.layout.height > 0 ? _root.layout.height : 844f;
            bool low = r.center.y > h * 0.5f;
            _arrow.style.display = DisplayStyle.Flex;
            _arrow.style.left = r.center.x - 15f;
            _arrow.style.top = low ? r.yMin - pad - 34f : r.yMax + pad + 4f;
            _arrow.style.rotate = new Rotate(new Angle(low ? 0f : 180f, AngleUnit.Degree));
        }

        void Pulse()
        {
            if (_ring.style.display == DisplayStyle.None) return;
            float t = Settings.ReducedMotion ? 0.5f : 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            _ring.style.opacity = 0.55f + 0.45f * t;
            _arrow.style.translate = new Translate(0f, Settings.ReducedMotion ? 0f : (t - 0.5f) * 8f);
        }

        void ScrollIntoView(VisualElement target)
        {
            if (_scrolledTo == target) return;
            _scrolledTo = target;
            for (var p = target.parent; p != null; p = p.parent)
                if (p is ScrollView scroll)
                {
                    scroll.ScrollTo(target);
                    return;
                }
        }

        // ---------- where to point ----------

        /// <summary>A named control that's on screen right now.</summary>
        VisualElement? Find(string name, VisualElement? within = null)
        {
            var e = (within ?? _root).Q(name);
            return Shown(e) ? e : null;
        }

        static bool Shown(VisualElement? e)
        {
            if (e == null || e.panel == null) return false;
            var r = e.worldBound;
            if (float.IsNaN(r.width) || r.width < 2f || r.height < 2f) return false;
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.opacity < 0.05f
                    || p.resolvedStyle.visibility == Visibility.Hidden)
                    return false;
            return true;
        }

        Hint HintFor(TutorialStepDef step, GameState state, UIController ui)
        {
            switch (step.Id)
            {
                case "resources": return new Hint(Find("tut-resources"));
                case "build-quartz": return BuildHint(state, ui, BuildingId.QuartzExtractor, 1);
                case "queues": return ToBase(ui) ?? new Hint(Find("tut-queues-fab"));
                case "claim": return ClaimHint(state, ui);
                case "command-center": return BuildHint(state, ui, BuildingId.CommandCenter, 2);
                case "speed-up": return SpeedUpHint(state, ui, BuildingId.CommandCenter);
                case "path": return PathHint(state, ui);
                case "fleet": return CloseOthers(ui) ?? new Hint(Find("tut-nav-FLEET"));
                case "map": return CloseOthers(ui) ?? new Hint(Find("tut-nav-MAP"));
                case "find-camp": return FindCampHint(ui);
                case "spy": return SpyHint(state, ui);
                case "intel": return MailHint(ui, "tut-mail-spy");
                case "attack": return AttackHint(state, ui);
                case "report": return Find("tut-replay-report") is { } rep ? new Hint(rep) : MailHint(ui, "tut-mail-battle");
                case "spoils": return ClaimHint(state, ui);
                case "research": return ResearchHint(state, ui);
                case "districts": return ToBase(ui) ?? new Hint(Find("tut-districts"));
                case "more":
                    return ToBase(ui) ?? new Hint(Find("tut-more-fab"), null, ui.MoreOpen ? "Tap ••• again to close it" : null);
                default: return default;
            }
        }

        /// <summary>A panel is up that the step doesn't use: point at its ×.</summary>
        Hint? CloseOthers(UIController ui)
        {
            if (ui.CurrentModal is { } modal && Find("tut-close", modal) is { } close)
                return new Hint(close);
            return null;
        }

        /// <summary>Back to the colony: close what's open, then BASE.</summary>
        Hint? ToBase(UIController ui)
        {
            if (CloseOthers(ui) is { } close) return close;
            if (ui.View != ViewId.Base) return new Hint(Find("tut-nav-BASE"));
            return null;
        }

        Hint BuildHint(GameState state, UIController ui, BuildingId id, int level)
        {
            if (Find($"tut-upgrade-{id}") is { } upgrade) return new Hint(upgrade, null, Affordability(state, id, level));
            if (BuildingMarkers.Selected == id && Find("tut-ring-upgrade") is { } ring)
                return new Hint(ring, null, Affordability(state, id, level));
            if (ToBase(ui) is { } back) return back;
            return PadHint(state, id);
        }

        /// <summary>The building's pad on the globe, or the district tab that turns to it.</summary>
        Hint PadHint(GameState state, BuildingId id)
        {
            if (BuildingMarkers.PadPoint(id) is { } p)
                return new Hint(null, new Rect(p.x - 48f, p.y - 88f, 96f, 100f));
            var district = BaseLayout.BuildingPad(id).District;
            return new Hint(Find($"tut-district-{district}"));
        }

        static string? Affordability(GameState state, BuildingId id, int level)
        {
            int current = state.Buildings[id].Level;
            if (current >= level) return null;
            var check = BuildingSystem.CheckUpgrade(state, id);
            return check.Ok ? null : check.Reason;
        }

        Hint ClaimHint(GameState state, UIController ui)
        {
            var quest = QuestSystem.Current(state);
            if (quest == null) return default;
            if (!QuestSystem.IsComplete(state, quest))
            {
                string wait = quest.Goal == QuestGoal.BuildingLevel ? BuildWait(state, quest.Building) : "";
                return Hint.Say(wait.Length > 0 ? $"The reward unlocks when it's built: {wait}" : "Almost there…");
            }
            if (Find("tut-claim") is { } claim) return new Hint(claim);
            if (ToBase(ui) is { } back) return back;
            return new Hint(Find("tut-quest"));
        }

        static string BuildWait(GameState state, BuildingId id)
        {
            var order = state.BuildQueue.FirstOrDefault(o => o.Building == id && o.MineId == null);
            if (order == null) return "";
            return order.EndsAtTick > 0 ? UiTheme.FmtDuration(Math.Max(0, order.EndsAtTick - state.Tick)) : "waiting for a free slot";
        }

        Hint SpeedUpHint(GameState state, UIController ui, BuildingId id)
        {
            if (Find($"tut-use-{Tutorial.SpeedupItem}") is { } use) return new Hint(use);
            if (Find($"tut-speedup-{id}") is { } speed) return new Hint(speed);
            // The quick-action ring's BOOST opens the speed-ups straight away.
            if (BuildingMarkers.Selected == id && Find("tut-ring-boost") is { } boost) return new Hint(boost);
            if (ToBase(ui) is { } back) return back;
            return PadHint(state, id);
        }

        Hint PathHint(GameState state, UIController ui)
        {
            var quest = QuestSystem.Current(state);
            if (quest == null) return default;
            string progress = $"Path: {quest.Title}";
            if (QuestSystem.IsComplete(state, quest)) return ClaimHint(state, ui).With(progress + " — ready to claim");
            if (quest.Goal != QuestGoal.BuildingLevel) return Hint.Say(progress);
            string wait = BuildWait(state, quest.Building);
            if (wait.Length > 0)
            {
                bool token = state.Inventory.Any(i => i.ItemId == Tutorial.SpeedupItem && i.Count > 0);
                return Hint.Say($"{progress} · {Buildings.Defs[quest.Building].Name} ready in {wait}" +
                    (token ? ". You have a Speed-Up left if you want it now." : ""));
            }
            return BuildHint(state, ui, quest.Building, quest.Target).With(progress);
        }

        Hint FindCampHint(UIController ui)
        {
            if (Find("tut-search-find") is { } find)
                return SearchPanel.Picked == NodeKind.Camp ? new Hint(find) : new Hint(Find($"tut-search-{NodeKind.Camp}"));
            if (CloseOthers(ui) is { } close) return close;
            if (ui.View != ViewId.Map) return new Hint(Find("tut-nav-MAP"));
            return new Hint(Find("tut-find-fab"));
        }

        Hint SpyHint(GameState state, UIController ui)
        {
            var probe = state.Marches.FirstOrDefault(m => m.Mission == MarchMission.Spy);
            if (probe != null)
                return Hint.Say($"Probe on its way: arrives in {UiTheme.FmtDuration(Math.Max(0, probe.ArrivesAtTick - state.Tick))}");
            if (Find("tut-spy") is { } spy) return new Hint(spy);
            return FindCampHint(ui);
        }

        Hint MailHint(UIController ui, string row)
        {
            if (Find(row) is { } r) return new Hint(r);
            if (CloseOthers(ui) is { } close) return close; // a report or another panel is up
            return new Hint(Find("tut-nav-MAIL"));
        }

        Hint AttackHint(GameState state, UIController ui)
        {
            var fleet = state.Marches.FirstOrDefault(m => m.Mission == MarchMission.Attack);
            if (fleet != null)
                return Hint.Say($"Fleet on its way: the battle starts in {UiTheme.FmtDuration(Math.Max(0, fleet.ArrivesAtTick - state.Tick))}");
            if (Find("tut-launch") is { } launch)
                return NodeComposer.SelectedCount > 0
                    ? new Hint(launch, null, "Check the forecast, then ATTACK")
                    : new Hint(Find("tut-all-docked"), null, "Tap ALL DOCKED to send your Fighters");
            if (Find("tut-attack") is { } attack) return new Hint(attack);
            if (Find("tut-view-on-map") is { } view) return new Hint(view); // the scan is open
            // The camp you scanned, on the map: tap it.
            if (ScannedCamp(state) is { } tile && ui.CurrentModal == null
                && _ctx.GetComponent<MapView>() is { } map && map.PanelPoint(tile) is { } p)
                return new Hint(null, new Rect(p.x - 34f, p.y - 34f, 68f, 68f));
            return FindCampHint(ui);
        }

        static TileXY? ScannedCamp(GameState state)
        {
            foreach (var m in state.Mailbox)
                if (m is SpyReport r && r.Intel.Kind == NodeKind.Camp && r.Intel.Garrison != null)
                    return r.Target;
            return null;
        }

        Hint ResearchHint(GameState state, UIController ui)
        {
            // Rewards waiting on the Path pay for this: claim them first.
            if (QuestSystem.Current(state) is { } quest && QuestSystem.IsComplete(state, quest))
                return ClaimHint(state, ui).With("Claim your Path reward first: it pays for this");
            if (state.Buildings[BuildingId.ResearchLab].Level < 1)
            {
                string wait = BuildWait(state, BuildingId.ResearchLab);
                if (wait.Length > 0) return Hint.Say($"Research Lab ready in {wait}");
                return BuildHint(state, ui, BuildingId.ResearchLab, 1);
            }
            if (Find("tut-research") is { } go) return new Hint(go);
            var pick = TutorialSystem.SuggestedTech(state);
            if (pick is { } tech && Find($"tut-tech-{tech}") is { } card)
                return new Hint(card, null, $"Try {Techs.Defs[tech].Name}");
            // The tech is on another page: its tab (ECONOMY 0, COMBAT 1, DEFENSE 2).
            if (pick is { } other)
            {
                var cat = Techs.Defs[other].Category;
                int page = cat == TechCategory.Military ? 1 : cat == TechCategory.Defense ? 2 : 0;
                if (Find($"tut-research-tab-{page}") is { } tab) return new Hint(tab, null, $"Try {Techs.Defs[other].Name}");
            }
            if (Find("tut-open-research") is { } open) return new Hint(open);
            if (BuildingMarkers.Selected == BuildingId.ResearchLab && Find("tut-ring-info") is { } info) return new Hint(info);
            if (ToBase(ui) is { } back) return back;
            if (pick == null) return Hint.Say("Save up a little: no research is affordable yet");
            return PadHint(state, BuildingId.ResearchLab);
        }
    }
}
