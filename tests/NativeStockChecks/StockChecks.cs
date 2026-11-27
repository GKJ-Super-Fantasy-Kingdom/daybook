using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static string savePath;
    private const string HashlessPrimary = "{\"runID\":\"primary\",\"hash\":\"\",\"coins\":14}";
    private const string HashlessBackup = "{\"runID\":\"backup\",\"hash\":\"\",\"coins\":11}";

    private static bool UseFixtureSavePath(ref string __result)
    {
        __result = savePath;
        return false;
    }

    private static void CheckSaveHook(Assembly daybook, Harmony patches)
    {
        var savePatch = daybook.GetType("Daybook.GameCheckpoints+Save", true);
        patches.PatchAll(savePatch);
        var save = patches.GetPatchedMethods().Single(method => method.DeclaringType == typeof(SaveManager));
        var parameters = save.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        var hasBackupFlag = parameters.SequenceEqual(new[] { typeof(string), typeof(string), typeof(bool) });
        if ((!hasBackupFlag && !parameters.SequenceEqual(new[] { typeof(string), typeof(string) })) ||
            Harmony.GetPatchInfo(save).Postfixes.Count(patch => patch.owner == patches.Id) != 1)
            throw new Exception("Expected one Daybook postfix on the exact native save overload");

        // Redirect only filename resolution. The installed game still writes and rotates the files.
        var pathGetter = AccessTools.Method(typeof(SaveManager), "GetFilePath", new[] { typeof(string) });
        patches.Patch(pathGetter, prefix: new HarmonyMethod(typeof(StockChecks), "UseFixtureSavePath"));
        savePath = Path.Combine(Path.GetDirectoryName(daybook.Location), "1_human_game.data");
        var manager = EmptyManager<SaveManager>();
        File.WriteAllText(savePath, "first primary");
        File.WriteAllText(savePath + ".bak", "older backup");
        if (hasBackupFlag)
        {
            save.Invoke(manager, new object[] { "human_game", "second primary", false });
            if (File.ReadAllText(savePath) != "second primary" || File.ReadAllText(savePath + ".bak") != "older backup")
                throw new Exception("Save without backup changed the backup or failed to write");
        }
        save.Invoke(manager, hasBackupFlag
            ? new object[] { "human_game", "saved with backup", true }
            : new object[] { "human_game", "saved with backup" });
        if (File.ReadAllText(savePath) != "saved with backup" ||
            File.ReadAllText(savePath + ".bak") != (hasBackupFlag ? "second primary" : "first primary"))
            throw new Exception("Native save backup rotation changed");
        Console.WriteLine("PASS one checkpoint postfix on {0} and native primary/backup writes", save);
        CheckReadHook(daybook, patches, manager);
    }

    // Embedded Mono has no Unity player. Stub only its JSON boundary, keeping the game's real disk and fallback methods.
    private static bool ParseFixture(string json, Type type, ref object __result)
    {
        if (type != typeof(GameData)) return true;
        if (json == HashlessPrimary || json == HashlessBackup)
            __result = new GameData { version = 4, hash = "", runID = json == HashlessPrimary ? "primary" : "backup" };
        else if (json == "legacy hashed fixture")
            __result = new GameData { version = 4, hash = "legacy hash" };
        else
            throw new ArgumentException("Malformed native fixture");
        return false;
    }

    private static void CheckReadHook(Assembly daybook, Harmony patches, SaveManager manager)
    {
        var hook = daybook.GetType("Daybook.GameCheckpoints", true);
        foreach (var name in new[] { "ReadPayload", "SelectRead" })
            patches.PatchAll(hook.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic));
        var parse = AccessTools.Method(typeof(UnityEngine.JsonUtility), "FromJson", new[] { typeof(string), typeof(Type) });
        patches.Patch(parse, prefix: new HarmonyMethod(typeof(StockChecks), "ParseFixture"));
        var selectedKey = AccessTools.Method(hook, "SelectedKey");
        var key = AccessTools.Method(daybook.GetType("Daybook.Journal", true), "CheckpointKey");
        File.WriteAllText(savePath, HashlessPrimary);
        File.WriteAllText(savePath + ".bak", HashlessBackup);
        var primary = manager.LoadGameData("human_game", true, true, false);
        if (primary.runID != "primary" ||
            !Equals(selectedKey.Invoke(null, new object[] { primary }), key.Invoke(null, new object[] { "", HashlessPrimary })))
            throw new Exception("Native's selected hashless primary did not identify its exact payload");

        File.WriteAllText(savePath, "malformed");
        var backup = manager.LoadGameData("human_game", true, true, false);
        if (backup.runID != "backup" ||
            !Equals(selectedKey.Invoke(null, new object[] { backup }), key.Invoke(null, new object[] { "", HashlessBackup })) ||
            Equals(selectedKey.Invoke(null, new object[] { primary }), selectedKey.Invoke(null, new object[] { backup })) ||
            File.ReadAllText(savePath) != "malformed" || File.ReadAllText(savePath + ".bak") != HashlessBackup)
            throw new Exception("Native fallback did not retain the selected backup's identity and bytes");

        try
        {
            manager.LoadGameData("human_game", false, true, false);
            throw new InvalidOperationException("Malformed fixture unexpectedly loaded with fallback disabled");
        }
        catch (ArgumentException) { }
        File.WriteAllText(savePath, "legacy hashed fixture");
        var legacy = manager.LoadGameData("human_game", true, true, false);
        if (!Equals(selectedKey.Invoke(null, new object[] { legacy }), key.Invoke(null, new object[] { "legacy hash", null })))
            throw new Exception("Native hashed checkpoint identity changed after a failed load");
        Console.WriteLine("PASS native primary and fallback backup select their exact hashless payload; legacy keys remain readable");
    }

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
        AccessTools.Field(balance, "Now").SetValue(b, 1);
        refresh.Invoke(panel, new object[] { "Live Day 4", true });
        if (net.text != "-2" || net.color != spent.color || net.color == earned.color)
            throw new Exception("Loss must retain its sign and match the spending color");
        AccessTools.Field(balance, "Now").SetValue(b, 3);
        refresh.Invoke(panel, new object[] { "Live Day 4", true });
        if (net.text != "0" || net.color == spent.color || net.color == earned.color)
            throw new Exception("Unchanged stock must use a distinct neutral color");
        Console.WriteLine("PASS signed and neutral changes in the ledger");
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
            CheckSaveHook(daybook, patches);
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
