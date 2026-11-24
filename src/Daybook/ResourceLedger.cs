using System;
using System.Collections.Generic;
using System.Linq;
using SuperFantasyKingdom;

namespace Daybook
{
    internal sealed class LedgerEntry
    {
        internal readonly ResourceType Type;
        internal readonly ResourceData Data;
        internal readonly string Group;
        internal Balance Balance = new Balance();
        internal string Label;
        internal readonly string Asset;
        internal readonly UnityEngine.Sprite Icon;

        internal LedgerEntry(ResourceType type, ResourceData data, string group, string label = null)
        {
            Type = type;
            Data = data;
            Group = group;
            Asset = data == null ? null : data.name;
            Icon = data == null ? null : data.icon;
            Label = label ?? (data != null && data.stringTitle != null ? data.GetName() : null);
            if (string.IsNullOrEmpty(Label))
                Label = type == ResourceType.SuperMetal ? "Super Metal" :
                    Enum.IsDefined(typeof(ResourceType), type) ? type.ToString() : $"Resource {(int)type}";
        }
    }

    internal sealed class ResourceLedger
    {
        internal List<LedgerEntry> Entries { get; private set; } = new List<LedgerEntry>();
        internal int HeaderCount { get; private set; }
        internal int Revision { get; private set; }
        internal readonly ResourceManager Kingdom;
        internal readonly RaceManager Race;
        private readonly HashSet<ResourceType> notified = new HashSet<ResourceType>();

        internal ResourceLedger(ResourceManager kingdom, RaceManager race)
        {
            Kingdom = kingdom;
            Race = race;
            RefreshCatalog();
        }

        private readonly List<KeyValuePair<int, Balance>> removed = new List<KeyValuePair<int, Balance>>();

        private static Balance CopyRow(LedgerEntry entry)
        {
            var balance = entry.Balance.Copy();
            balance.Asset = entry.Asset;
            balance.Label = entry.Label;
            balance.Group = entry.Group;
            return balance;
        }

        private void RememberRemoved(int key, Balance balance)
        {
            removed.RemoveAll(pair => pair.Key == key && pair.Value.Asset == balance.Asset);
            removed.Add(new KeyValuePair<int, Balance>(key, balance));
        }

        internal bool RefreshCatalog()
        {
            var next = new List<LedgerEntry>();
            var seen = new HashSet<ResourceType>();
            void Add(ResourceType type, ResourceData data, string group, bool universal = false)
            {
                if (data != null && data.type == ResourceType.None) data = null;
                if (data == null && !universal) return;
                // Kingdom equivalents use the returned asset's stock key, also used by the HUD.
                if (data != null) type = data.type;
                if (type == ResourceType.None || !seen.Add(type)) return;
                var entry = new LedgerEntry(type, data, group);
                var old = Entries.Find(item => item.Type == type && item.Asset == entry.Asset);
                if (old != null) entry.Balance = old.Balance;
                next.Add(entry);
            }

            var header = Race.GetMainResources();
            if (header != null)
                foreach (var data in header)
                    if (data != null) Add(data.type, data, "Materials");
            int headerCount = next.Count;
            var foods = Kingdom.GetFoods();
            if (foods != null)
                foreach (var type in foods) Add(type, Race.GetResourceData(type), "Food and supplies");
            foreach (var type in new[] { ResourceType.Flour, ResourceType.Water })
                Add(type, Race.GetResourceData(type), "Food and supplies");
            foreach (var type in new[] { ResourceType.Coins, ResourceType.Faith })
                Add(type, Race.GetResourceData(type), "Currency", true);
            foreach (var type in new[] { ResourceType.Shards, ResourceType.Stars, ResourceType.SuperMetal })
                Add(type, Race.GetResourceData(type), "Progression", true);

            void AddStock(ResourceType type)
            {
                if (seen.Contains(type)) return;
                var data = Race.GetResourceData(type);
                if (data != null) Add(type, data, data.isFood ? "Food and supplies" : "Other stock");
            }
            // ResourceType also names terrain, workers and caravan factions. A ResourceData
            // object alone does not establish that the current kingdom can store the resource.
            var buildings = CityManager.Instance?.GetBuildings();
            if (buildings != null)
                foreach (var building in buildings)
                {
                    var slots = building?.GetStorage()?.GetStorageSpaces();
                    if (slots == null) continue;
                    for (int i = 0; i < slots.Length; i++)
                        if (slots[i] > 0) AddStock((ResourceType)i);
                }
            var starting = Kingdom.GetStartingResourcesDictionary();
            if (starting != null)
                foreach (var type in starting.Keys)
                    if (!Enum.IsDefined(typeof(ResourceType), type)) AddStock(type);
            foreach (var type in notified) AddStock(type);

            bool changed = HeaderCount != headerCount || Entries.Count != next.Count;
            for (int i = 0; !changed && i < next.Count; i++)
            {
                var old = Entries[i];
                var item = next[i];
                changed = old.Type != item.Type || old.Data != item.Data || old.Asset != item.Asset ||
                    old.Group != item.Group || old.Label != item.Label || old.Icon != item.Icon;
            }
            if (!changed) return false;
            foreach (var old in Entries)
                if (old.Balance.Now.HasValue && !next.Exists(e => e.Type == old.Type && e.Asset == old.Asset))
                    RememberRemoved((int)old.Type, CopyRow(old));
            foreach (var item in next)
            {
                if (Entries.Exists(e => e.Type == item.Type && e.Asset == item.Asset)) continue;
                var match = FindSaved(removed, item, next);
                if (match == null) continue;
                item.Balance = match.Copy();
                // Preserve observed transactions without attributing changes during an absence.
                item.Balance.ObservationGap = true;
                item.Balance.Now = Amount(item.Type);
                removed.RemoveAll(pair => pair.Value == match);
            }
            HeaderCount = headerCount;
            Entries = next;
            Revision++;
            return true;
        }

        internal bool Changed(ResourceType type)
        {
            var data = Race.GetResourceData(type);
            var stock = data != null && data.type != ResourceType.None ? data.type : type;
            var entry = Entries.Find(e => e.Type == stock);
            bool catalogChanged = false;
            if (data != null && data.type != ResourceType.None &&
                (entry != null ? entry.Asset != data.name : !Enum.IsDefined(typeof(ResourceType), stock)))
            {
                notified.Add(type);
                catalogChanged = RefreshCatalog();
            }
            return Read(stock) || catalogChanged;
        }

        // Prefer the stock slot. Only migrate between slots when the asset name is unique on both sides.
        private static Balance FindSaved(List<KeyValuePair<int, Balance>> saved, LedgerEntry entry,
            List<LedgerEntry> catalog)
        {
            foreach (var pair in saved)
                if (pair.Key == (int)entry.Type && pair.Value.SameResource(entry.Asset))
                    return pair.Value;
            if (string.IsNullOrEmpty(entry.Asset) || catalog.Count(e => e.Asset == entry.Asset) != 1)
                return null;
            Balance match = null;
            foreach (var pair in saved)
                if (pair.Value.Asset == entry.Asset)
                {
                    if (match != null) return null;
                    match = pair.Value;
                }
            return match;
        }

        internal List<LedgerEntry> HistoryRows(DayRecord record)
        {
            var rows = new List<LedgerEntry>();
            var saved = record.AllRows().ToList();
            foreach (var pair in saved)
            {
                var type = (ResourceType)pair.Key;
                var live = Entries.Find(e => e.Type == type && pair.Value.SameResource(e.Asset));
                if (live == null && !string.IsNullOrEmpty(pair.Value.Asset))
                    live = Entries.Find(e => e.Asset == pair.Value.Asset && FindSaved(saved, e, Entries) == pair.Value);
                var group = pair.Value.Group ?? live?.Group ?? "Archived stock";
                var entry = new LedgerEntry(type, live?.Data, group, pair.Value.Label ?? live?.Label);
                entry.Balance = pair.Value;
                rows.Add(entry);
            }
            return rows;
        }

        private int? Amount(ResourceType type) => type == ResourceType.Faith && FaithManager.Instance == null
            ? (int?)null : Kingdom.GetResourceAvailableAmount(type);

        internal bool Read(ResourceType? changed = null)
        {
            bool dirty = false;
            foreach (var entry in Entries)
            {
                if (changed.HasValue && entry.Type != changed.Value) continue;
                dirty |= entry.Balance.Observe(Amount(entry.Type));
            }
            return dirty;
        }

        internal DayRecord Snapshot(int day)
        {
            var record = new DayRecord { Day = day };
            foreach (var entry in Entries)
                record.Rows.Add((int)entry.Type, CopyRow(entry));
            if (removed.Count > 0)
                record.Removed = new List<KeyValuePair<int, Balance>>(removed);
            return record;
        }

        internal void BeginDay(int day, DayRecord saved)
        {
            removed.Clear();
            var rows = saved != null && saved.Day == day ? saved.AllRows().ToList() : null;
            var matched = new HashSet<Balance>();
            foreach (var entry in Entries)
            {
                var amount = entry.Balance.Now;
                var prior = rows == null ? null : FindSaved(rows, entry, Entries);
                if (prior != null) matched.Add(prior);
                entry.Balance = prior != null && amount.HasValue && prior.Now == amount
                    ? prior.Copy() : new Balance { Dawn = amount, Now = amount };
            }
            if (rows != null)
                foreach (var pair in rows)
                    if (!matched.Contains(pair.Value)) RememberRemoved(pair.Key, pair.Value.Copy());
        }
    }
}
