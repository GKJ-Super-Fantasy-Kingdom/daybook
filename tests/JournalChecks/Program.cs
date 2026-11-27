using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Daybook;

internal static class Program
{
    private static readonly Guid Run = new Guid("66d2c7f3-62df-48e6-a7ea-ae85cb987cdc");
    private static readonly Guid PayloadRun = new Guid("7201c6c9-887f-4425-a17c-207fac1ad5d5");
    private static readonly string Key = Journal.Fingerprint("native checkpoint");
    private const string Payload = "{\"day\":6,\"coins\":14,\"hash\":\"\"}";

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }

    private static void Main(string[] args)
    {
        if (args.Length == 1)
        {
            VerifyAfterRestart(args[0]);
            return;
        }
        var root = Directory.CreateTempSubdirectory("daybook-checks-");
        try
        {
            var journal = new Journal(root.FullName, 1, 0, Run);
            CatalogChecks.Run(journal);
            CheckPaging(root.FullName);
            CheckPayloadCheckpoints(root.FullName);
            var boards = new Balance { Dawn = 3, Now = 3 };
            var record = new DayRecord { Day = 7, Rows = { [1] = boards } };
            journal.Save(record);
            boards.Observe(11);
            boards.Observe(3);
            Require(!boards.Observe(3), "duplicate notification");
            journal.SaveCheckpoint(Key, record);
            boards.Observe(2);
            journal.Save(record);
            Require(journal.Previous().Length == 0, "live day excluded from history");

            var child = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            child.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            child.ArgumentList.Add(root.FullName);
            using var process = Process.Start(child);
            process.WaitForExit();
            Require(process.ExitCode == 0, "fresh process readback");
            Console.WriteLine("Passed resource catalog, accounting, journal write, restart, rollback, history and isolation checks.");
        }
        finally { root.Delete(true); }
    }

    private static void CheckPayloadCheckpoints(string root)
    {
        var journal = new Journal(root, 3, 1, PayloadRun);
        const string unsaved = "{\"day\":6,\"coins\":11,\"hash\":\"\"}";
        var key = Journal.CheckpointKey("", Payload);
        var otherKey = Journal.CheckpointKey("", unsaved);
        Require(key != otherKey && key == Journal.CheckpointKey(null, Payload) &&
            Journal.CheckpointKey(null, null) == null, "hashless identity is based on the selected payload");
        var record = new DayRecord { Day = 7, Rows = { [12] = new Balance { Dawn = 11, Now = 14, Earned = 5, Spent = 2 } } };
        journal.Save(record);
        journal.SaveCheckpoint(key, record);
        journal.SaveCheckpoint(key, record);

        var sameDay = new Journal(root, 3, 1, PayloadRun);
        sameDay.ResumeAttempt(sameDay.LoadCheckpoint(key), 7);
        Require(sameDay.Previous().Length == 0, "same checkpoint and day reuse the unchanged attempt");

        record.Rows[12].Observe(11);
        journal.Save(record);
        var morning = new Journal(root, 3, 1, PayloadRun);
        var path = morning.Previous()[0];
        Require(morning.Read(path).Rows[12].Spent == 5, "unsaved changes reached live per-day file");
        Require(morning.LoadCheckpoint(otherKey) == null, "same day with different payload cannot select the saved ledger");
        morning.FinalizeAttempt(key, 8);
        Require(morning.Read(path).Rows[12].Spent == 5, "unrelated dawn cannot finalize the night checkpoint");
        morning.FinalizeAttempt(key, 7);
        morning.FinalizeAttempt(key, 7);
        var coins = morning.Read(path).Rows[12];
        Require(morning.Previous().Length == 1 && coins.Now == 14 && coins.Earned == 5 && coins.Spent == 2,
            "tavern transition restores the committed totals to the original page exactly once");
        Require(Directory.GetFiles(Path.GetDirectoryName(path), "save-payload-*.json").Length == 1,
            "repeated native Save with the same payload keeps one sidecar");
        Require(new Journal(root, 3, 0, PayloadRun).LoadCheckpoint(key) == null &&
            new Journal(root, 4, 1, PayloadRun).LoadCheckpoint(key) == null &&
            new Journal(root, 3, 1, Guid.NewGuid()).LoadCheckpoint(key) == null, "payload keys remain scoped to slot, kingdom and run");

        const string nativeHash = "archived native hash";
        string legacyPath = Path.Combine(Path.GetDirectoryName(path), "save-" + Journal.Fingerprint(nativeHash) + ".json");
        File.Copy(path, legacyPath);
        var legacy = morning.LoadCheckpoint(Journal.CheckpointKey(nativeHash, Payload));
        Require(legacy.Rows[12].Now == 14 && legacy.Attempt == null, "older hashed sidecars still load without attempt metadata");
    }

    private static void CheckPaging(string root)
    {
        var run = Guid.NewGuid();
        var journal = new Journal(root, 2, 0, run);
        var record = new DayRecord { Day = 7, Rows = { [1] = new Balance { Dawn = 1, Now = 1 } } };
        journal.Save(record);
        var reader = new Journal(root, 2, 0, run);
        var saved = reader.Previous()[0];
        var bad = Path.Combine(root, "corrupt.json");
        File.WriteAllText(bad, "{");
        var missing = Path.Combine(root, "missing.json");
        var paths = new[] { missing, saved, bad };
        int index = -1, errors = 0;
        DayRecord page = null;
        reader.Browse(ref paths, ref index, ref page, 0, e => errors++);
        Require(index == 0 && paths.Length == 2 && page.Day == 7, "skip missing newer file and open valid page");
        var current = page;
        reader.Browse(ref paths, ref index, ref page, 1, e => errors++);
        Require(page == current && index == 0 && paths.Length == 1 && errors == 1,
            "invalid oldest pruned without losing current page");
        paths = new[] { bad, saved };
        index = 1;
        reader.Browse(ref paths, ref index, ref page, 0, e => errors++);
        Require(page == current && index == 0 && paths.Length == 1, "pruning newer file keeps selected page");
        paths = new[] { bad, missing };
        index = -1;
        page = null;
        reader.Browse(ref paths, ref index, ref page, 0, e => errors++);
        Require(paths.Length == 0 && index == -1 && page == null, "all-invalid archive disables paging");
    }

    private static void VerifyAfterRestart(string root)
    {
        var payloadJournal = new Journal(root, 3, 1, PayloadRun);
        var hashless = payloadJournal.LoadCheckpoint(Journal.CheckpointKey("", Payload));
        var page = payloadJournal.Read(payloadJournal.Previous()[0]).Rows[12];
        Require(payloadJournal.Previous().Length == 1 && hashless.Rows[12].Now == 14 && page.Spent == 2,
            "hashless checkpoint and its single finalized history page survive process restart");
        var journal = new Journal(root, 1, 0, Run);
        var checkpoint = journal.LoadCheckpoint(Key);
        var boards = checkpoint.Rows[1];
        Require(boards.Dawn == 3 && boards.Now == 3 && boards.Earned == 8 && boards.Spent == 8,
            "offsetting transactions survive restart");
        boards.Asset = "boards";
        boards.Label = "Planks";
        boards.Group = "Materials";
        journal.Save(checkpoint);
        var reader = new Journal(root, 1, 0, Run);
        var written = reader.Read(reader.Previous()[0]).Rows[1];
        Require(written.Label == "Planks" && written.Group == "Materials" && written.Asset == "boards",
            "historical presentation survives removal");
        Require(journal.LoadCheckpoint(Journal.Fingerprint("another save")) == null, "older or unknown save");
        var paths = journal.Previous();
        Require(paths.Length == 1 && journal.Read(paths[0]).Rows[1].Spent == 9,
            "archived observation survives rollback");
        foreach (var other in new[] { new Journal(root, 2, 0, Run), new Journal(root, 1, 1, Run),
                                     new Journal(root, 1, 0, Guid.NewGuid()) })
            Require(other.Previous().Length == 0 && other.LoadCheckpoint(Key) == null, "run isolation");
        journal.Save(checkpoint);
        Require(File.Exists(paths[0]), "replayed day keeps previous attempt");
        File.WriteAllText(paths[0], "{");
        try { journal.Read(paths[0]); }
        catch (SerializationException)
        {
            Require(journal.LoadCheckpoint(Key).Rows[1].Spent == 8, "corruption isolated");
            return;
        }
        throw new InvalidOperationException("corrupt record was accepted");
    }
}
