using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using SuperFantasyKingdom;

internal static class StockChecks
{
    private static readonly Dictionary<ResourceType, object> rows = new Dictionary<ResourceType, object>();
    private static ResourceManager stock;
    private static FaithManager faith;
    private static Type balance;

    private static T EmptyManager<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }

    private static void Reset(ResourceType kind, int dawn)
    {
        var row = Activator.CreateInstance(balance, true);
        AccessTools.Field(balance, "Dawn").SetValue(row, dawn);
        AccessTools.Field(balance, "Now").SetValue(row, dawn);
        rows[kind] = row;
    }

    private static void Sample(ResourceType kind)
    {
        int amount = kind == ResourceType.Faith ? faith.GetFaith() : stock.GetResourceAvailableAmount(kind);
        AccessTools.Method(balance, "Observe").Invoke(rows[kind], new object[] { amount });
    }

    private static void Expect(ResourceType kind, long earned, long spent, int now)
    {
        var row = rows[kind];
        if ((long)AccessTools.Field(balance, "Earned").GetValue(row) != earned ||
            (long)AccessTools.Field(balance, "Spent").GetValue(row) != spent ||
            (int)AccessTools.Field(balance, "Now").GetValue(row) != now)
            throw new Exception("Incorrect gross or final stock for " + kind);
        Console.WriteLine("PASS {0}: earned {1}, spent {2}, now {3}", kind, earned, spent, now);
    }

    // JIT the calling methods after installing the hooks, as the loader does in-game.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DirectWrites()
    {
        foreach (var kind in new[] { ResourceType.Coins, ResourceType.Shards, ResourceType.Stars,
                                    ResourceType.SuperMetal, ResourceType.Faith })
            Reset(kind, 0);
        stock.SetCoins(8);
        stock.AddToCoins(-8);
        stock.SetShards(8);
        stock.AddToShards(-8);
        stock.SetStars(8);
        stock.AddToStars(-8);
        stock.SetSuperMetal(8);
        stock.AddToSuperMetal(-8);
        faith.SetFaith(8);
        faith.SetFaith(0);
        foreach (var kind in rows.Keys) Expect(kind, 8, 8, 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Reward(int earned)
    {
        Reset(ResourceType.Coins, 0);
        stock.AddResource(ResourceType.Coins, 5);
        stock.SpendResource(ResourceType.Coins, 2);
        Sample(ResourceType.Coins);
        Expect(ResourceType.Coins, earned, 2, earned - 2);
        stock.SetCoins(0);
    }

    // Exercise the shipped HUD formatter without a Unity scene or graphical device.
    private sealed class TextSink : UnityEngine.UI.Text
    {
        public override string text { get; set; }
        public override UnityEngine.Color color { get; set; }
    }

    private static TextSink AttachText(object owner, string field)
    {
        var text = EmptyManager<TextSink>();
        AccessTools.Field(owner.GetType(), field).SetValue(owner, text);
        return text;
    }

    private static void CheckPartialHud(Assembly daybook)
    {
        var panelType = daybook.GetType("Daybook.DaybookPanel", true);
        var panel = FormatterServices.GetUninitializedObject(panelType);
        var entryType = daybook.GetType("Daybook.LedgerEntry", true);
        var entry = FormatterServices.GetUninitializedObject(entryType);
        var b = Activator.CreateInstance(balance, true);
        AccessTools.Field(entryType, "Balance").SetValue(entry, b);
        AccessTools.Field(entryType, "Label").SetValue(entry, "Modded ore");
        AccessTools.Field(balance, "Earned").SetValue(b, 4L);
        AccessTools.Field(balance, "Spent").SetValue(b, 2L);
        AccessTools.Field(balance, "Now").SetValue(b, 5);
        var row = Activator.CreateInstance(panelType.GetNestedType("Row", BindingFlags.NonPublic), true);
        AccessTools.Field(row.GetType(), "Entry").SetValue(row, entry);
        var rowsField = AccessTools.Field(panelType, "rows");
        var panelRows = (IList)Activator.CreateInstance(rowsField.FieldType);
        panelRows.Add(row);
        rowsField.SetValue(panel, panelRows);
        var earned = AttachText(row, "Earned");
        var spent = AttachText(row, "Spent");
        var net = AttachText(row, "Net");
        var dawn = AttachText(row, "Dawn");
        var title = AttachText(row, "Title");
        AttachText(row, "Now");
        AttachText(panel, "historyCaption");
        var refresh = AccessTools.Method(panelType, "Refresh");
        refresh.Invoke(panel, new object[] { "Day 3", true });
        if (title.text != "Modded ore*" || earned.text != "4" || spent.text != "2" ||
            dawn.text != "\u2013" || net.text != "\u2013") throw new Exception("Late resource HUD values");
        AccessTools.Field(balance, "Dawn").SetValue(b, 3);
        refresh.Invoke(panel, new object[] { "Day 4", true });
        if (title.text != "Modded ore" || net.text != "+2") throw new Exception("Complete resource HUD values");
        AccessTools.Field(balance, "ObservationGap").SetValue(b, true);
        refresh.Invoke(panel, new object[] { "Saved Day 4", true });
        if (title.text != "Modded ore*" || earned.text != "4" || spent.text != "2" || net.text != "+2")
            throw new Exception("Gap-only change did not repaint the HUD");
        refresh.Invoke(panel, new object[] { "Waiting for dawn", false });
        if (earned.text != "\u2013" || spent.text != "\u2013") throw new Exception("Pre-dawn counters");
        Console.WriteLine("PASS partial, complete, archived and pre-dawn HUD text");
    }

    private static int Main(string[] args)
    {
        var daybook = Assembly.LoadFrom(args[0]);
        balance = daybook.GetType("Daybook.Balance", true);
        var hooks = daybook.GetType("Daybook.DirectStockChanges", true);
        var patches = new Harmony("local.daybook.stock-checks");
        var other = new Harmony("local.daybook.reward-multiplier");
        Action<ResourceType> sample = Sample;
        var signal = hooks.GetEvent("Changed", BindingFlags.NonPublic | BindingFlags.Static);
        try
        {
            stock = EmptyManager<ResourceManager>();
            faith = EmptyManager<FaithManager>();
            StatisticsManager.Instance = EmptyManager<StatisticsManager>();
            AccessTools.Method(hooks, "Install").Invoke(null, new object[] { patches });
            signal.GetAddMethod(true).Invoke(null, new object[] { sample });
            ResourceManager.OnResourceAmountChanged += sample;
            CheckPartialHud(daybook);
            DirectWrites();
            Reward(5);
            if (args.Length > 1)
            {
                var cheat = Assembly.LoadFrom(args[1]);
                var type = cheat.GetType("CheatMenu.Cheats.CoinMultiplier", true);
                var folder = Path.GetDirectoryName(daybook.Location);
                AccessTools.Method(typeof(BepInEx.Paths), "SetExecutablePath").Invoke(null, new object[] {
                    Path.Combine(folder, "fixture.exe"), folder, Path.GetDirectoryName(typeof(ResourceManager).Assembly.Location),
                    new string[0]
                });
                var config = new ConfigFile(Path.Combine(folder, "test.cfg"), false);
                AccessTools.Method(type, "Bind").Invoke(null, new object[] { config });
                ((ConfigEntry<float>)AccessTools.Field(type, "Multiplier").GetValue(null)).Value = 3;
                other.PatchAll(type.GetNestedType("ResourceManager_AddResource", BindingFlags.Public | BindingFlags.NonPublic));
                Reward(15);
            }
            return 0;
        }
        finally
        {
            ResourceManager.OnResourceAmountChanged -= sample;
            signal.GetRemoveMethod(true).Invoke(null, new object[] { sample });
            other.UnpatchSelf();
            patches.UnpatchSelf();
        }
    }
}
