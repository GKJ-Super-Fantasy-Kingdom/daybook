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
    [BepInPlugin("sfk.daybook", "Daybook", "0.4.0")]
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
        private float nextWrite, nextPoll;
        private string lastError;
        private Harmony patches;
        internal string LoadedHash;

        private void Awake()
        {
            showOverlay = Config.Bind("Display", "ShowOverlay", true, "Show resource balances and changes since dawn.");
            GameCheckpoints.Plugin = this;
            patches = new Harmony("sfk.daybook.checkpoints");
            patches.PatchAll(typeof(GameCheckpoints.Load));
            patches.PatchAll(typeof(GameCheckpoints.Save));
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
            GameCheckpoints.Plugin = null;
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
                dirty |= ledger.Read();
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
            dirty |= ledger.Read();
            journal.SaveCheckpoint(Journal.Fingerprint(hash), ledger.Snapshot(day));
            Flush();
        }

        private void ResourceChanged(ResourceType type)
        {
            if (tracking && ledger.Kingdom != null && ledger.Kingdom == ResourceManager.Instance &&
                GameManager.Instance != null && !GameManager.Instance.IsUnloading())
                dirty |= ledger.Read(type);
        }

        private bool BrowseHistory(bool expanded)
        {
            int target = historyIndex;
            var keys = Keyboard.current;
            if (!expanded) target = -1;
            else if (keys != null && keys.pageUpKey.wasPressedThisFrame)
                target = Math.Min(previous.Length - 1, historyIndex + 1);
            else if (keys != null && keys.pageDownKey.wasPressedThisFrame)
                target = Math.Max(-1, historyIndex - 1);
            if (target == historyIndex) return false;
            history = null;
            int step = target > historyIndex ? 1 : -1;
            while (target >= 0 && target < previous.Length)
            {
                try { history = journal.Read(previous[target]); }
                catch (Exception e) { ReportJournalError(e); }
                if (history != null) break;
                target += step;
            }
            historyIndex = history == null ? -1 : target;
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
                if (ledger.Read() && tracking) dirty = true;
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
            bool changedView = panel.SyncView(expanded);
            changedView |= BrowseHistory(expanded);
            if (!opening && !changedView && Time.unscaledTime < nextRefresh)
                return;

            nextRefresh = Time.unscaledTime + 0.5f;
            string caption = history == null ? (tracking ? $"Day {day}" : "Waiting for dawn")
                : $"Saved day {history.Day} ({historyIndex + 1}/{previous.Length})";
            if (expanded && previous.Length > 0) caption += "   PgUp older / PgDn newer";
            panel.Refresh(history, caption);
        }
    }
}
