// Boot + persistence orchestration for the single-player game (replaces the
// retired NetBootstrap): boot lands on the TITLE SCREEN (user spec — the old
// login page's slot): CONTINUE GAME resumes the saved galaxy, NEW GAME founds
// a fresh one (double-confirmed when a save exists). Afterwards: autosave
// every 30 s (atomic write + rotating backup, written off the main thread) and
// a synchronous save-on-background, since mobile lifecycles never fire a clean
// quit.
using System;
using UnityEngine;
using GalaxyRoyale.Local;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Local Bootstrap")]
    [RequireComponent(typeof(GameContext))]
    public sealed class LocalBootstrap : MonoBehaviour
    {
        public const float AutosaveSeconds = 30f;

        public static LocalBootstrap? Instance { get; private set; }

        GameContext _ctx = null!;
        float _nextAutosave;
        bool _booted;
        /// <summary>Save decoded at boot, waiting for the title-screen choice.</summary>
        (GameState state, long savedAtMs, BotGalaxy? bots)? _pendingLoad;

        void Awake()
        {
            Instance = this;
            _ctx = GetComponent<GameContext>();
        }

        void Start()
        {
            // Back from another app: GameContext caught the galaxy up — say so.
            _ctx.Resumed += debrief => { if (_booted) ReportAway(debrief); };

            _pendingLoad = LocalSave.Load();
            if (_pendingLoad is { } peek && peek.bots == null)
                _pendingLoad = null; // unreadable/pre-pivot leftovers → treat as no save
            // Returning commanders resume STRAIGHT into the base view (user spec
            // 2026-07-07) — the title page only greets a fresh install (or a
            // post-reset boot); starting over lives behind the profile's RESET.
            if (_pendingLoad != null) ContinueGame();
            else UI.TitlePanel.Open(_ctx);
        }

        /// <summary>The title screen asks; the placeholder sim idles until a choice lands.</summary>
        public bool HasSave => _pendingLoad != null;

        /// <summary>"Commander X · might N · saved 2h ago" for the CONTINUE button.</summary>
        public string SaveSummary
        {
            get
            {
                if (_pendingLoad is not { } found) return "";
                long might = PowerSystem.ComputePower(found.state);
                long agoSec = Math.Max(0,
                    (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - found.savedAtMs) / 1000);
                return $"Commander {found.state.Profile.Name} · might {might:N0} · " +
                       $"saved {UI.UiTheme.FmtDuration(agoSec)} ago";
            }
        }

        /// <summary>CONTINUE GAME — adopt the saved galaxy and fast-forward offline time.</summary>
        public void ContinueGame()
        {
            if (_pendingLoad is not { } found || found.bots == null) { NewGame(); return; }
            _ctx.AdoptState(found.state, found.bots, found.savedAtMs);
            _pendingLoad = null;
            FinishBoot();
            ReportAway(_ctx.LastDebrief);
        }

        /// <summary>NEW GAME — erase any save and found a fresh galaxy. The title
        /// screen double-confirms before calling this when a save exists.</summary>
        public void NewGame()
        {
            LocalSave.Delete();
            _pendingLoad = null;
            StartFreshGalaxy();
            FinishBoot();
            var ui = UI.UIController.Instance;
            ui?.Toast($"Welcome to the galaxy, Commander — {BotSystem.BotCount} rivals await");
            ui?.ShowRookieHints();
        }

        void FinishBoot()
        {
            UI.UIController.Instance?.CloseModal(); // drop the title page
            _booted = true;
            _nextAutosave = Time.time + AutosaveSeconds;
        }

        /// <summary>Found a brand-new galaxy: rim spawn for the player, then the rivals.</summary>
        public void StartFreshGalaxy()
        {
            // Device-local side tables outlive the save file — a new galaxy must
            // not inherit the old one's pending raids or daily progress.
            RaidArrivals.ClearPending();
            DailyObjectives.ResetProgress();
            UI.RankingsPanel.ForgetLastRank();
            var state = GameState.CreateNewGame(Spawn.GalaxySeed);
            // A random founder token gives each new game its own rim spawn angle.
            string founder = $"player-{UnityEngine.Random.Range(int.MinValue, int.MaxValue)}";
            state.HomeTile = Spawn.SpawnTileFor(founder, Spawn.GalaxySeed);
            var bots = BotSystem.CreateGalaxy(Spawn.GalaxySeed, state.HomeTile);
            _ctx.AdoptState(state, bots, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            SaveNow();
        }

        /// <summary>Double-confirmed by the profile panel before this is called.</summary>
        public void ResetEmpire()
        {
            LocalSave.Delete();
            StartFreshGalaxy();
        }

        void Update()
        {
            if (!_booted || _ctx.State == null) return;
            if (Time.time < _nextAutosave) return;
            _nextAutosave = Time.time + AutosaveSeconds;
            LocalSave.SaveInBackground(_ctx.State!, _ctx.Bots,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _booted && _ctx.State != null) SaveNow();
        }

        void OnApplicationQuit()
        {
            if (_booted && _ctx.State != null) SaveNow();
        }

        /// <summary>Immediate save — profile edits call this so nothing rides on the 30 s timer.</summary>
        public static void RequestSync()
        {
            var inst = Instance;
            if (inst == null || !inst._booted || inst._ctx.State == null) return;
            inst.SaveNow();
        }

        void SaveNow()
        {
            LocalSave.Save(_ctx.State!, _ctx.Bots,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        /// <summary>"While you were away": the full report when something happened
        /// over a real absence (10+ min), otherwise the short resources toast.
        /// A panel the player left open on resume is never yanked away.</summary>
        void ReportAway(OfflineDebrief? debrief)
        {
            if (debrief == null || debrief.ElapsedSec < 60) return;
            var ui = UI.UIController.Instance;
            if (debrief.Notable && debrief.ElapsedSec >= 600 && ui != null && !ui.HasModal)
            {
                UI.DebriefPanel.Open(_ctx, debrief);
                return;
            }
            ToastOffline(debrief);
        }

        void ToastOffline(OfflineDebrief summary)
        {
            if (summary.ElapsedSec < 60) return;
            var parts = new System.Collections.Generic.List<string>();
            if (summary.Gained.Gold > 0) parts.Add($"+{UI.UiTheme.FmtAmount(summary.Gained.Gold)} gold");
            if (summary.Gained.Quartz > 0) parts.Add($"+{UI.UiTheme.FmtAmount(summary.Gained.Quartz)} quartz");
            if (summary.Gained.Helium > 0) parts.Add($"+{UI.UiTheme.FmtAmount(summary.Gained.Helium)} helium");
            string gains = parts.Count > 0 ? $" — {string.Join(", ", parts)}" : "";
            UI.UIController.Instance?.Toast(
                $"While you were away ({UI.UiTheme.FmtDuration(summary.ElapsedSec)}){gains}");
        }
    }
}
