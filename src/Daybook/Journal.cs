using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace Daybook
{
    [DataContract]
    internal sealed class Balance
    {
        [DataMember] internal int? Dawn, Now;
        [DataMember] internal long Earned, Spent;

        internal bool Observe(int? amount)
        {
            if (Now == amount) return false;
            if (Now.HasValue && amount.HasValue)
            {
                long difference = (long)amount.Value - Now.Value;
                if (difference > 0) Earned += difference;
                else Spent -= difference;
            }
            Now = amount;
            return true;
        }

        internal Balance Copy() => (Balance)MemberwiseClone();
    }

    [DataContract]
    internal sealed class DayRecord
    {
        [DataMember] internal int Version = 1;
        [DataMember] internal int Day;
        [DataMember] internal Dictionary<int, Balance> Rows = new Dictionary<int, Balance>();

        internal bool Matches(int day, Dictionary<int, Balance> live)
        {
            return Day == day && Rows.Count == live.Count &&
                live.All(pair => Rows.TryGetValue(pair.Key, out var saved) && saved.Now == pair.Value.Now);
        }
    }

    internal sealed class Journal
    {
        private readonly string directory;
        private readonly string current;
        private readonly DataContractJsonSerializer format = new DataContractJsonSerializer(typeof(DayRecord));
        internal Journal(string root, int profile, int race, Guid run)
        {
            directory = Path.Combine(root, profile.ToString(), race.ToString(), run.ToString("N"));
            current = Path.Combine(directory, $"day-{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.json");
        }

        internal void Save(DayRecord record) => Write(current, record);

        internal string[] Previous()
        {
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "day-*.json").Where(path => path != current)
                    .OrderByDescending(path => path, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();
        }

        internal static string Fingerprint(string json)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
        }

        private string Checkpoint(string fingerprint) => Path.Combine(directory, "save-" + fingerprint + ".json");

        internal void SaveCheckpoint(string fingerprint, DayRecord record) => Write(Checkpoint(fingerprint), record);
        internal DayRecord LoadCheckpoint(string fingerprint) => Read(Checkpoint(fingerprint));

        internal DayRecord Read(string path)
        {
            if (!File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
            {
                var data = format.ReadObject(stream) as DayRecord;
                if (data == null || data.Version != 1 || data.Rows == null || data.Rows.Count == 0 ||
                    data.Rows.Values.Any(row => row == null || row.Earned < 0 || row.Spent < 0))
                    throw new InvalidDataException("Unrecognized Daybook record.");
                return data;
            }
        }

        private void Write(string path, DayRecord record)
        {
            Directory.CreateDirectory(directory);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    format.WriteObject(stream, record);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
