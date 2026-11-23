using BepInEx;
using BepInEx.Configuration;
using SuperFantasyKingdom;
using SuperFantasyKingdom.UI;
using UnityEngine;

namespace Daybook
{
    [BepInPlugin("sfk.daybook", "Daybook", "0.3.1")]
    public sealed class DaybookPlugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> showOverlay;
        private ResourceLedger ledger;
        private CityManager detailedCity;
        private float nextRefresh;
        private DaybookPanel panel;
        private float nextBind;

        private void Awake()
        {
            showOverlay = Config.Bind("Display", "ShowOverlay", true, "Show resource balances and changes since dawn.");
            DaytimeManager.OnMorningStart += RecordDawn;
            CityManager.WorkerToggle += SetDetailedView;
            Logger.LogInfo("Daybook ready. Waiting for dawn.");
        }

        private void OnDestroy()
        {
            DaytimeManager.OnMorningStart -= RecordDawn;
            CityManager.WorkerToggle -= SetDetailedView;
            if (panel != null)
                Destroy(panel.gameObject);
        }

        private void SetDetailedView(bool enabled)
        {
            detailedCity = enabled ? CityManager.Instance : null;
        }

        private void RecordDawn(int day)
        {
            if (!BindLedger())
                return;
            ledger.Read(dawn: true);
            nextRefresh = 0;
            Logger.LogInfo($"Day {day} dawn recorded for {ledger.Entries.Count} resources.");
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
                ledger = new ResourceLedger(kingdom, race);
                nextRefresh = nextBind = 0;
            }
            return true;
        }

        private void LateUpdate()
        {
            var menu = PauseMenuManger.Instance;
            if (!showOverlay.Value || DaytimeManager.Instance == null ||
                (menu != null && (menu.InMenu() || menu.IsOpen())) || !BindLedger())
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
            bool changedView = panel.SyncView(detailedCity != null && detailedCity == CityManager.Instance);
            if (!opening && !changedView && Time.unscaledTime < nextRefresh)
                return;

            nextRefresh = Time.unscaledTime + 0.5f;
            ledger.Read();
            panel.Refresh();
        }
    }
}
