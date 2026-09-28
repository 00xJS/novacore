// Boot + persistence orchestration for the single-player game (replaces the
// retired NetBootstrap): boot lands on the TITLE SCREEN (user spec — the old
// login page's slot): CONTINUE GAME resumes the saved galaxy, NEW GAME founds
// a fresh one (double-confirmed when a save exists), RESTORE brings back an
// iCloud backup. Afterwards: autosave every 30 s (atomic write + rotating
// backup, written off the main thread), mirrored to iCloud every few minutes
// (CloudSave), and a synchronous save + backup on going to the background,
// since mobile lifecycles never fire a clean quit.
using System;
using UnityEngine;
using GalaxyRoyale.Data;
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
        /// <summary>An iCloud backup this much newer than the device's save (played
        /// on another device) is offered at boot instead of silently ignored.</summary>
        const long NewerCloudMarginMs = 2 * 60 * 1000;

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

            CloudSave.Sync(); // the iCloud backup's latest values land asynchronously
            _pendingLoad = LocalSave.Load();
            if (_pendingLoad is { } peek && peek.bots == null)
                _pendingLoad = null; // unreadable/pre-pivot leftovers → treat as no save
            // Returning commanders resume STRAIGHT into the base view (user spec
            // 2026-07-07) — the title page only greets a fresh install (or a
            // post-reset boot); starting over lives behind the profile's RESET.
            if (_pendingLoad is not { } local) { UI.TitlePanel.Open(_ctx); return; }

            // Played on another device since this one last saved? Offer that.
            var cloud = CloudSave.Peek();
            if (cloud != null && cloud.SavedAtMs > local.savedAtMs + NewerCloudMarginMs)
            {
                UI.ChoicePanel.Open("NEWER ICLOUD BACKUP",
                    $"iCloud holds a newer save: {CloudSummary(cloud)}.\n" +
                    $"This device: Commander {local.state.Profile.Name}, saved {Ago(local.savedAtMs)} ago.",
                    ("RESTORE ICLOUD BACKUP", () => { if (!RestoreFromCloud()) ContinueGame(); }),
                    ("KEEP THIS DEVICE'S SAVE", ContinueGame),
                    ContinueGame);
                return;
            }
            ContinueGame();
        }

        /// <summary>The iCloud backup's header, if there is one (title screen).</summary>
        public CloudSave.Header? CloudBackup => CloudSave.Peek();

        public static string CloudSummary(CloudSave.Header h) =>
            $"Commander {h.Name} · might {h.Might:N0} · backed up {Ago(h.SavedAtMs)} ago";

        static string Ago(long ms) => UI.UiTheme.FmtLong(
            Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ms) / 1000));

        /// <summary>Adopt the iCloud backup (title screen / boot prompt).
        /// Whatever this device held rotates to the .bak on the next save.</summary>
        public bool RestoreFromCloud()
        {
            var found = CloudSave.FetchSave();
            if (found == null || found.Value.bots == null)
            {
                UI.UIController.Instance?.Toast("The iCloud backup couldn't be read", UI.Icon.Warning, UI.UiTheme.Bad);
                return false;
            }
            var backup = found.Value;
            // Device-local side tables belong to whatever galaxy was here before.
            RaidArrivals.ClearPending();
            DailyObjectives.ResetProgress();
            UI.RankingsPanel.ForgetLastRank();
            _pendingLoad = backup;
            ContinueGame();
            SaveNow(); // land it on disk now, not on the next autosave
            UI.UIController.Instance?.Toast($"Empire restored from iCloud — welcome back, Commander {backup.state.Profile.Name}",
                UI.Icon.Check, UI.UiTheme.Good);
            return true;
        }

        /// <summary>BACK UP NOW (profile): save and push the backup immediately.</summary>
        public void BackUpNow()
        {
            if (!_booted || _ctx.State == null) return;
            SaveNow(forceCloud: true);
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
            if (_pendingLoad is not { } found || found.bots == null) { NewGame(Balance.TestModeDefault); return; }
            _ctx.AdoptState(found.state, found.bots, found.savedAtMs);
            _pendingLoad = null;
            FinishBoot();
            ReportAway(_ctx.LastDebrief);
        }

        /// <summary>NEW GAME — erase any save and found a fresh galaxy in the chosen
        /// mode and difficulty (NewGamePanel). The title screen double-confirms
        /// before offering this when a save exists.</summary>
        public void NewGame(bool testMode, Difficulty difficulty = Difficulty.Standard)
        {
            LocalSave.Delete();
            _pendingLoad = null;
            StartFreshGalaxy(testMode, difficulty);
            FinishBoot();
            var ui = UI.UIController.Instance;
            string level = difficulty == Difficulty.Standard ? "" : $" · {Difficulties.Name(difficulty)}";
            ui?.Toast(testMode
                ? $"TESTING galaxy founded{level} — {BotSystem.BotCount} rivals, a full war chest"
                : $"Welcome to the galaxy, Commander{level} — {BotSystem.BotCount} rivals await");
            ui?.ShowRookieHints();
        }

        void FinishBoot()
        {
            UI.UIController.Instance?.CloseModal(); // drop the title page
            _booted = true;
            _nextAutosave = Time.time + AutosaveSeconds;
            DebugLaunch.Run(_ctx);
        }

        /// <summary>True once a galaxy is loaded or founded (the title page is gone).</summary>
        public static bool Booted => Instance != null && Instance._booted;

        /// <summary>Found a brand-new galaxy: rim spawn for the player, then the rivals.</summary>
        public void StartFreshGalaxy(bool testMode, Difficulty difficulty = Difficulty.Standard)
        {
            // Device-local side tables outlive the save file — a new galaxy must
            // not inherit the old one's pending raids or daily progress.
            RaidArrivals.ClearPending();
            DailyObjectives.ResetProgress();
            UI.RankingsPanel.ForgetLastRank();
            var state = GameState.CreateNewGame(Spawn.GalaxySeed, testMode, difficulty);
            // A random founder token gives each new game its own rim spawn angle.
            string founder = $"player-{UnityEngine.Random.Range(int.MinValue, int.MaxValue)}";
            state.HomeTile = Spawn.SpawnTileFor(founder, Spawn.GalaxySeed);
            var bots = BotSystem.CreateGalaxy(Spawn.GalaxySeed, state.HomeTile);
            _ctx.AdoptState(state, bots, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            SaveNow();
        }

        /// <summary>Double-confirmed by the profile panel, mode and difficulty picked in NewGamePanel.</summary>
        public void ResetEmpire(bool testMode, Difficulty difficulty = Difficulty.Standard)
        {
            LocalSave.Delete();
            StartFreshGalaxy(testMode, difficulty);
        }

        void Update()
        {
            CloudSave.Flush(); // a backup packed on the save worker goes up from here
            if (!_booted || _ctx.State == null) return;
            if (Time.time < _nextAutosave) return;
            _nextAutosave = Time.time + AutosaveSeconds;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LocalSave.SaveInBackground(_ctx.State!, _ctx.Bots, now, cloud: CloudSave.Due(now, force: false));
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _booted && _ctx.State != null) SaveNow(forceCloud: true);
        }

        void OnApplicationQuit()
        {
            if (_booted && _ctx.State != null) SaveNow(forceCloud: true);
        }

        /// <summary>Immediate save — profile edits call this so nothing rides on the 30 s timer.</summary>
        public static void RequestSync()
        {
            var inst = Instance;
            if (inst == null || !inst._booted || inst._ctx.State == null) return;
            inst.SaveNow();
        }

        void SaveNow(bool forceCloud = false)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            LocalSave.Save(_ctx.State!, _ctx.Bots, now, cloud: CloudSave.Due(now, forceCloud));
            CloudSave.Flush(); // synchronous: iOS may suspend us right after
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
