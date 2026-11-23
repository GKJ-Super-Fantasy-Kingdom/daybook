using System;
using System.Collections.Generic;
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
            Console.WriteLine("Passed journal write, restart, rollback, history and isolation checks.");
        }
        finally { root.Delete(true); }
    }

    private static void VerifyAfterRestart(string root)
    {
        var journal = new Journal(root, 1, 0, Run);
        var checkpoint = journal.LoadCheckpoint(Key);
        var boards = checkpoint.Rows[1];
        Require(boards.Dawn == 3 && boards.Now == 3 && boards.Earned == 8 && boards.Spent == 8,
            "offsetting transactions survive restart");
        var live = new Dictionary<int, Balance> { [1] = new Balance { Now = 3 } };
        Require(checkpoint.Matches(7, live), "same checkpoint, day and inventory");
        Require(!checkpoint.Matches(8, live), "new dawn");
        live[1].Now = 2;
        Require(!checkpoint.Matches(7, live), "stock mismatch");
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
