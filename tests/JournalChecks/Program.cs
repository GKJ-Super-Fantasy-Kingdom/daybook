using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Daybook;

internal static class Program
{
    private static readonly Guid Run = new Guid("66d2c7f3-62df-48e6-a7ea-ae85cb987cdc");
    private static readonly string Key = Journal.Fingerprint("native checkpoint");

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
