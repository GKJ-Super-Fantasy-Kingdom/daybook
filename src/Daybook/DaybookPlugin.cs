using System;
using System.IO;
using HarmonyLib;
using UnityEngine.InputSystem;
using BepInEx;
using BepInEx.Configuration;
using SuperFantasyKingdom;
using SuperFantasyKingdom.UI;
using UnityEngine;

namespace Daybook
{
    [BepInPlugin("sfk.daybook", "Daybook", "0.4.2")]
    public sealed class DaybookPlugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> showOverlay;
        private ResourceLedger ledger;
        private CityManager detailedCity;
        private float nextRefresh;
        private DaybookPanel panel;
        private float nextBind;

        private Journal journal;
        private DayRecord history;
        private string[] previous = Array.Empty<string>();
        private int historyIndex = -1;
        private int day;
        private Guid run;
        private bool tracking, dirty;
        private float nextWrite, nextPoll, nextCatalog;
        private string lastError;
        private Harmony patches;
        internal string LoadedHash;

        private void Awake()
        {
            showOverlay = Config.Bind("Display", "ShowOverlay", true, "Show resource balances and changes since dawn.");
            ControllerLedger.Plugin = this;
            GameCheckpoints.Plugin = this;
            patches = new Harmony("sfk.daybook.checkpoints");
            patches.PatchAll(typeof(GameCheckpoints.Load));
            patches.PatchAll(typeof(GameCheckpoints.Save));
            patches.PatchAll(typeof(ControllerLedger.OlderAction));
            patches.PatchAll(typeof(ControllerLedger.NewerAction));
            patches.PatchAll(typeof(ControllerLedger.HudAction));
            DirectStockChanges.Install(patches);
            DirectStockChanges.Changed += ResourceChanged;
            GameManager.OnGameInit += CloseDay;
            GameManager.BeforeSave += CaptureAndFlush;
            ResourceManager.OnResourceAmountChanged += ResourceChanged;
            DaytimeManager.OnMorningStart += RecordDawn;
            CityManager.WorkerToggle += SetDetailedView;
            Logger.LogInfo("Daybook ready. Waiting for dawn.");
        }

        private void OnDestroy()
        {
            CloseDay();
            ControllerLedger.Plugin = null;
            GameCheckpoints.Plugin = null;
            DirectStockChanges.Changed -= ResourceChanged;
            patches?.UnpatchSelf();
            GameManager.OnGameInit -= CloseDay;
            GameManager.BeforeSave -= CaptureAndFlush;
            ResourceManager.OnResourceAmountChanged -= ResourceChanged;
            DaytimeManager.OnMorningStart -= RecordDawn;
            CityManager.WorkerToggle -= SetDetailedView;
            if (panel != null)
                Destroy(panel.gameObject);
        }

        private void SetDetailedView(bool enabled)
        {
            detailedCity = enabled ? CityManager.Instance : null;
        }

        private void OnApplicationQuit()
        {
            CaptureAndFlush();
            tracking = false;
        }

        private void CaptureAndFlush()
        {
            if (tracking && ledger.Kingdom != null && ledger.Kingdom == ResourceManager.Instance &&
                GameManager.Instance != null && !GameManager.Instance.IsUnloading())
            {
                dirty |= ledger.RefreshCatalog();
                dirty |= ledger.Read();
            }
            Flush();
        }

        private void CloseDay()
        {
            Flush();
            tracking = dirty = false;
            journal = null;
            history = null;
            historyIndex = -1;
            previous = Array.Empty<string>();
            LoadedHash = null;
        }

        private void RecordDawn(int newDay)
        {
            // Finish the old record before sampling the new kingdom or dawn.
            string loaded = LoadedHash;
            CloseDay();
            if (!BindLedger()) return;
            day = newDay;
            ledger.RefreshCatalog();
            ledger.Read();
            DayRecord restored = null;
            if (Guid.TryParse(GameDataManager.Instance.GetRunID(), out run))
            {
                journal = new Journal(Path.Combine(Application.persistentDataPath, "Daybook"),
                    SettingsManager.Instance.GetProfile() + 1, (int)ledger.Race.GetRace(), run);
                try
                {
                    previous = journal.Previous();
                    if (!string.IsNullOrEmpty(loaded))
                        restored = journal.LoadCheckpoint(Journal.Fingerprint(loaded));
                }
                catch (Exception e) { ReportJournalError(e); }
            }
            ledger.BeginDay(day, restored);
            tracking = dirty = true;
            nextRefresh = nextWrite = 0;
            Flush();
        }

        internal void ReportJournalError(Exception error)
        {
            if (lastError != error.Message) Logger.LogWarning("Daybook journal: " + error.Message);
            lastError = error.Message;
        }

        private void Flush()
        {
            if (!dirty || journal == null || ledger == null) return;
            try
            {
                journal.Save(ledger.Snapshot(day));
                dirty = false;
                lastError = null;
            }
            catch (Exception e) { ReportJournalError(e); }
        }

        internal void Checkpoint(string savedRun, string hash)
        {
            if (!tracking || journal == null || ledger.Kingdom != ResourceManager.Instance ||
                !Guid.TryParse(savedRun, out var id) || id != run || string.IsNullOrEmpty(hash)) return;
            dirty |= ledger.RefreshCatalog();
            dirty |= ledger.Read();
            journal.SaveCheckpoint(Journal.Fingerprint(hash), ledger.Snapshot(day));
            Flush();
        }

        private void ResourceChanged(ResourceType type)
        {
            if (tracking && ledger.Kingdom != null && ledger.Kingdom == ResourceManager.Instance &&
                GameManager.Instance != null && !GameManager.Instance.IsUnloading())
                dirty |= ledger.Changed(type);
        }

        private bool CanBrowseHistory() => previous.Length > 0 && panel != null &&
            panel.isActiveAndEnabled && UIManager.Instance != null && UIManager.Instance.IsWorldInteractable();

        internal bool CanPageWithController()
        {
            var player = ControllerLedger.ActivePlayer();
            var menu = PauseMenuManger.Instance;
            return player != null && showOverlay.Value && CanBrowseHistory() &&
                detailedCity != null && detailedCity == CityManager.Instance &&
                player.actions.FindAction(ControllerLedger.Details)?.IsPressed() == true &&
                (menu == null || (!menu.InMenu() && !menu.IsOpen()));
        }

        internal void PageHistory(int step)
        {
            int target = Math.Max(-1, Math.Min(previous.Length - 1, historyIndex + step));
            if (SelectHistory(target)) nextRefresh = 0;
        }

        private bool SelectHistory(int target)
        {
            if (target == historyIndex) return false;
            if (target >= 0)
                return journal.Browse(ref previous, ref historyIndex, ref history, target, ReportJournalError);
            history = null;
            historyIndex = -1;
            return true;
        }

        private bool BindLedger()
        {
            var kingdom = ResourceManager.Instance;
            var race = RaceManager.Instance;
            if (kingdom == null || race == null || CityManager.Instance == null)
                return false;
            if (ledger == null || ledger.Kingdom != kingdom || ledger.Race != race)
            {
                if (panel != null)
                {
                    panel.gameObject.SetActive(false);
                    Destroy(panel.gameObject);
                    panel = null;
                }
                if (tracking) CloseDay();
                ledger = new ResourceLedger(kingdom, race);
                nextRefresh = nextBind = 0;
            }
            return true;
        }

        private void LateUpdate()
        {
            bool bound = DaytimeManager.Instance != null && GameManager.Instance != null &&
                GameManager.Instance.IsGameLoaded() && !GameManager.Instance.IsUnloading() && BindLedger();
            if (bound && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + 0.5f;
                bool catalogChanged = false;
                if (Time.unscaledTime >= nextCatalog)
                {
                    nextCatalog = Time.unscaledTime + 2;
                    catalogChanged = ledger.RefreshCatalog();
                }
                if ((ledger.Read() || catalogChanged) && tracking) dirty = true;
            }
            if (dirty && Time.unscaledTime >= nextWrite)
            {
                nextWrite = Time.unscaledTime + 2;
                Flush();
            }
            var menu = PauseMenuManger.Instance;
            if (!showOverlay.Value || !bound ||
                (menu != null && (menu.InMenu() || menu.IsOpen())))
            {
                if (panel != null && panel.gameObject.activeSelf)
                    panel.gameObject.SetActive(false);
                return;
            }

            if (panel == null)
            {
                if (Time.unscaledTime < nextBind)
                    return;
                nextBind = Time.unscaledTime + 1;
                var header = FindObjectOfType<UIHeader>();
                if (header == null || (panel = DaybookPanel.Attach(header, ledger)) == null)
                    return;
                Logger.LogInfo($"Resource HUD attached: {ledger.HeaderCount} header counters, {ledger.Entries.Count} ledger rows.");
                nextRefresh = 0;
            }

            bool opening = !panel.gameObject.activeSelf;
            if (opening)
                panel.gameObject.SetActive(true);
            bool expanded = detailedCity != null && detailedCity == CityManager.Instance;
            var player = ControllerLedger.ActivePlayer();
            if (player != null)
                expanded &= player.actions.FindAction(ControllerLedger.Details)?.IsPressed() == true;
            bool canBrowse = expanded && CanBrowseHistory();
            bool showHistoryHints = canBrowse && player != null;
            bool changedView = false;
            var keys = Keyboard.current;
            if (!expanded) changedView |= SelectHistory(-1);
            else if (canBrowse && player == null && keys != null)
            {
                if (keys.pageUpKey.wasPressedThisFrame) PageHistory(1);
                else if (keys.pageDownKey.wasPressedThisFrame) PageHistory(-1);
            }
            changedView |= panel.SelectRows(ledger, history);
            bool recording = tracking || history != null;
            changedView |= panel.SyncView(expanded, showHistoryHints, recording);
            if (!opening && !changedView && Time.unscaledTime < nextRefresh)
                return;

            nextRefresh = Time.unscaledTime + 0.5f;
            string caption = history == null ? (tracking ? $"Live Day {day}" : "Waiting for dawn")
                : $"Saved Day {history.Day} ({historyIndex + 1}/{previous.Length})";
            bool older = historyIndex + 1 < previous.Length;
            bool newer = historyIndex >= 0;
            if (canBrowse && player == null && keys != null)
            {
                if (older) caption += "   PgUp Older";
                if (newer) caption += older ? " / PgDn Newer" : "   PgDn Newer";
            }
            panel.Refresh(caption, recording);
            if (showHistoryHints) panel.UpdateControllerHints(player, older, newer);
        }
    }
}
