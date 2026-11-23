using System.Collections.Generic;
using SuperFantasyKingdom;

namespace Daybook
{
    internal sealed class LedgerEntry
    {
        internal readonly ResourceType Type;
        internal readonly ResourceData Data;
        internal readonly string Group;
        internal Balance Balance = new Balance();

        internal LedgerEntry(ResourceType type, ResourceData data, string group)
        {
            Type = type;
            Data = data;
            Group = group;
        }
    }

    internal sealed class ResourceLedger
    {
        internal readonly List<LedgerEntry> Entries = new List<LedgerEntry>();
        internal readonly int HeaderCount;
        internal readonly ResourceManager Kingdom;
        internal readonly RaceManager Race;

        internal ResourceLedger(ResourceManager kingdom, RaceManager race)
        {
            Kingdom = kingdom;
            Race = race;
            foreach (var data in race.GetMainResources())
                if (data != null) Add(data.type, data, "Materials");
            HeaderCount = Entries.Count;

            foreach (var type in new[] { ResourceType.Wheat, ResourceType.Wood, ResourceType.Board,
                                         ResourceType.Stone, ResourceType.Gold, ResourceType.Ingot })
                Add(type, race.GetResourceData(type), "Materials");
            foreach (var type in kingdom.GetFoods())
                Add(type, race.GetResourceData(type), "Food and supplies");
            Add(ResourceType.Flour, race.GetResourceData(ResourceType.Flour), "Food and supplies");
            Add(ResourceType.Water, race.GetResourceData(ResourceType.Water), "Food and supplies");
            foreach (var type in new[] { ResourceType.Coins, ResourceType.Faith })
                Add(type, race.GetResourceData(type), "Currency", universal: true);
            foreach (var type in new[] { ResourceType.Shards, ResourceType.Stars, ResourceType.SuperMetal })
                Add(type, race.GetResourceData(type), "Progression", universal: true);
        }

        private void Add(ResourceType type, ResourceData data, string group, bool universal = false)
        {
            if (data != null && data.type == ResourceType.None)
                data = null;
            if ((data != null || universal) && !Entries.Exists(entry => entry.Type == type))
                Entries.Add(new LedgerEntry(type, data, group));
        }

        internal bool Read(ResourceType? changed = null)
        {
            bool dirty = false;
            foreach (var entry in Entries)
            {
                if (changed.HasValue && entry.Type != changed.Value) continue;
                int? amount = entry.Type == ResourceType.Faith && FaithManager.Instance == null
                    ? (int?)null : Kingdom.GetResourceAvailableAmount(entry.Type);
                dirty |= entry.Balance.Observe(amount);
            }
            return dirty;
        }

        internal DayRecord Snapshot(int day)
        {
            var record = new DayRecord { Day = day };
            foreach (var entry in Entries)
                record.Rows.Add((int)entry.Type, entry.Balance.Copy());
            return record;
        }

        internal void BeginDay(int day, DayRecord saved)
        {
            bool restore = saved != null && saved.Matches(day, Snapshot(day).Rows);
            foreach (var entry in Entries)
            {
                int? amount = entry.Balance.Now;
                entry.Balance = restore ? saved.Rows[(int)entry.Type].Copy()
                    : new Balance { Dawn = amount, Now = amount };
            }
        }
    }
}
