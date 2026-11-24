using System;
using System.Collections.Generic;
using System.Linq;
using Daybook;
using SuperFantasyKingdom;

// Only the inventory and metadata API surface used by ResourceLedger is simulated here.
namespace UnityEngine { internal class Sprite { } }
namespace SuperFantasyKingdom.Buildings { internal enum BuildingType { None, Store } }
namespace SuperFantasyKingdom
{
    internal enum ResourceType { None, Flour, Water, Coins, Faith, Shards, Stars, SuperMetal, Worker, Terrain, Ore }
    internal sealed class ResourceData
    {
        internal ResourceType type;
        internal string name, stringTitle;
        internal UnityEngine.Sprite icon;
        internal Buildings.BuildingType buildingToTakeFrom;
        internal bool isFood;
        internal string GetName() => stringTitle;
    }
    internal sealed class Storage
    {
        internal int[] Slots = new int[11];
        internal int[] GetStorageSpaces() => Slots;
    }
    internal sealed class Building
    {
        internal Storage Stock = new Storage();
        internal Storage GetStorage() => Stock;
    }
    internal sealed class CityManager
    {
        internal static CityManager Instance { get; } = new CityManager();
        internal List<Building> Buildings = new List<Building>();
        internal List<Building> GetBuildings() => Buildings;
    }
    internal class FaithManager { internal static FaithManager Instance { get; } = new FaithManager(); }
    internal sealed class RaceManager
    {
        internal readonly List<ResourceData> Header = new List<ResourceData>();
        internal readonly Dictionary<ResourceType, ResourceData> Assets = new Dictionary<ResourceType, ResourceData>();
        internal List<ResourceData> GetMainResources() => Header;
        internal ResourceData GetResourceData(ResourceType key) => Assets.GetValueOrDefault(key);
    }
    internal sealed class ResourceManager
    {
        internal List<ResourceType> GetFoods() => new List<ResourceType>();
        internal readonly Dictionary<ResourceType, int> Stock = new Dictionary<ResourceType, int>();
        internal Dictionary<ResourceType, int> GetStartingResourcesDictionary() => Stock;
        internal int GetResourceAvailableAmount(ResourceType key) => Stock.GetValueOrDefault(key);
    }
}

internal static class CatalogChecks
{
    private static void Check(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
    }

    private static void CheckPartialCoverage(Journal journal)
    {
        var race = new RaceManager();
        var stock = new ResourceManager();
        var key = (ResourceType)200;
        var asset = new ResourceData { type = key, name = "late", stringTitle = "Late stock" };
        var ledger = new ResourceLedger(stock, race);
        ledger.Read();
        ledger.BeginDay(1, null);

        race.Assets[key] = asset;
        stock.Stock[key] = 3;
        ledger.Changed(key);
        stock.Stock[key] = 7;
        ledger.Changed(key);
        stock.Stock[key] = 5;
        ledger.Changed(key);
        var late = ledger.Snapshot(1).Rows[(int)key];
        Check(late.HasSample && late.IsPartial && late.Dawn == null && late.Now == 5 &&
            late.Earned == 4 && late.Spent == 2, "first sighting keeps observed gross with unknown dawn");

        stock.Stock[key] = 10;
        ledger.Read();
        ledger.BeginDay(2, null);
        stock.Stock[key] = 12;
        ledger.Changed(key);
        race.Assets.Remove(key);
        ledger.RefreshCatalog();
        stock.Stock[key] = 21;
        race.Assets[key] = asset;
        ledger.RefreshCatalog();
        var resumed = ledger.Snapshot(2);
        var balance = resumed.Rows[(int)key];
        Check(balance.IsPartial && balance.Dawn == 10 && balance.Now == 21 &&
            balance.Earned == 2 && balance.Spent == 0, "returning asset marks unexplained stock as partial");
        journal.SaveCheckpoint("partial", resumed);
        ledger.BeginDay(2, journal.LoadCheckpoint("partial"));
        Check(ledger.Snapshot(2).Rows[(int)key].ObservationGap, "gap survives matching Continue");
        Check(ledger.HistoryRows(journal.LoadCheckpoint("partial")).Single(e => e.Type == key).Balance.IsPartial,
            "archived row preserves partial coverage");
        Check(new Balance { Dawn = 10, Now = 21, Earned = 2 }.IsPartial, "recognize gaps in older journals");
        ledger.BeginDay(3, null);
        Check(!ledger.Snapshot(3).Rows[(int)key].IsPartial, "new dawn restores full coverage");
    }

    internal static void Run(Journal journal)
    {
        CheckPartialCoverage(journal);
        var race = new RaceManager();
        var stock = new ResourceManager();
        var amber = new ResourceData { type = (ResourceType)100, name = "amber", stringTitle = "Amber",
            icon = new UnityEngine.Sprite(), buildingToTakeFrom = SuperFantasyKingdom.Buildings.BuildingType.Store };
        var salt = new ResourceData { type = (ResourceType)101, name = "salt", isFood = true };
        race.Assets[amber.type] = amber;
        race.Assets[salt.type] = salt;
        race.Assets[ResourceType.Flour] = amber;
        race.Header.Add(amber);
        stock.Stock[amber.type] = 12;
        stock.Stock[salt.type] = 0;
        foreach (var type in new[] { ResourceType.Worker, ResourceType.Terrain, ResourceType.Ore })
            race.Assets[type] = new ResourceData { type = type, name = type.ToString(),
                buildingToTakeFrom = SuperFantasyKingdom.Buildings.BuildingType.Store };
        stock.Stock[ResourceType.Worker] = 12;
        var building = new Building();
        building.Stock.Slots[(int)ResourceType.Ore] = 4;
        CityManager.Instance.Buildings.Add(building);
        var ledger = new ResourceLedger(stock, race);
        ledger.Changed(ResourceType.Worker);
        Check(ledger.Entries.Any(e => e.Type == ResourceType.Ore) &&
            !ledger.Entries.Any(e => e.Type == ResourceType.Worker || e.Type == ResourceType.Terrain),
            "only real storage slots expose additional native stock");
        ledger.Read();
        ledger.BeginDay(3, null);
        // A cap-limited grant actually moves two, then two are spent before the next poll.
        stock.Stock[amber.type] = 14;
        ledger.Changed(ResourceType.Flour);
        stock.Stock[amber.type] = 12;
        ledger.Changed(amber.type);
        race.Header.Insert(0, salt);
        amber.stringTitle = "Sunstone";
        amber.icon = null;
        ledger.RefreshCatalog();
        var snapshot = ledger.Snapshot(3);
        Check(ledger.HeaderCount == 2 && ledger.Entries[1].Type == amber.type &&
            snapshot.Rows[100].Earned == 2 && snapshot.Rows[100].Spent == 2 &&
            snapshot.Rows[100].Label == "Sunstone", "aliases, header order, rename and capped changes");

        var moved = new ResourceData { type = (ResourceType)105, name = "amber", stringTitle = "Sunstone" };
        var otherRace = new RaceManager();
        otherRace.Header.Add(moved);
        var otherStock = new ResourceManager();
        otherStock.Stock[moved.type] = 12;
        var renumbered = new ResourceLedger(otherStock, otherRace);
        renumbered.Read();
        renumbered.BeginDay(3, snapshot);
        Check(renumbered.Snapshot(3).Rows[105].Earned == 2, "unique asset survives slot reassignment on Continue");
        otherRace.Header.Add(new ResourceData { type = (ResourceType)106, name = "amber" });
        renumbered.RefreshCatalog();
        renumbered.Read();
        renumbered.BeginDay(3, snapshot);
        Check(renumbered.Snapshot(3).Rows[105].Earned == 0, "ambiguous asset name never borrows another balance");
        otherRace.Header.RemoveAt(1);
        renumbered.RefreshCatalog();
        otherStock.Stock[moved.type] = 11;
        renumbered.Read();
        renumbered.BeginDay(3, snapshot);
        Check(renumbered.Snapshot(3).Rows[105].Dawn == 11, "divergent stock gets a fresh baseline");
        var oldFormat = new DayRecord { Day = 3, Rows = { [105] = new Balance { Dawn = 10, Now = 11, Earned = 1 } } };
        renumbered.BeginDay(3, oldFormat);
        Check(renumbered.Snapshot(3).Rows[105].Earned == 1, "legacy save restored by stock slot");

        // The provider removes an asset, then another provider reuses its numeric slot.
        race.Header.Remove(amber);
        race.Assets.Remove(amber.type);
        race.Assets.Remove(ResourceType.Flour);
        ledger.RefreshCatalog();
        var replacement = new ResourceData { type = amber.type, name = "sulfur", stringTitle = "Sulfur" };
        race.Assets[replacement.type] = replacement;
        stock.Stock[replacement.type] = 5;
        ledger.RefreshCatalog();
        ledger.Read();
        var saved = ledger.Snapshot(3);
        string key = Journal.Fingerprint("catalog fixture");
        journal.SaveCheckpoint(key, saved);
        saved = journal.LoadCheckpoint(key);
        var resumed = new ResourceLedger(stock, race);
        resumed.Read();
        resumed.BeginDay(3, saved);
        var history = resumed.HistoryRows(resumed.Snapshot(3));
        Check(history.Single(e => e.Label == "Sunstone").Icon == null &&
            history.Single(e => e.Label == "Sunstone").Balance.Spent == 2 &&
            history.Single(e => e.Label == "Sulfur").Balance.Earned == 0, "vanished resource preserved across Continue");
        var legacy = new DayRecord { Day = 1, Rows = { [999] = new Balance { Now = 7 } } };
        Check(resumed.HistoryRows(legacy)[0].Label == "Resource 999", "legacy unavailable resource label");
        resumed.BeginDay(4, null);
        Check(resumed.Snapshot(4).Removed == null, "next dawn clears retired resources");
    }
}
