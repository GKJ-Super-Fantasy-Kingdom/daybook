using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Daybook
{
    [DataContract]
    internal sealed class Balance
    {
        [DataMember] internal int? Dawn, Now;
        [DataMember] internal long Earned, Spent;
        // Optional fields let older journals keep their numeric ResourceType keys.
        [DataMember(EmitDefaultValue = false)] internal string Asset, Label, Group;

        [DataMember(EmitDefaultValue = false)] internal bool ObservationGap;

        internal bool HasSample => Dawn.HasValue || Now.HasValue || Earned != 0 || Spent != 0;
        // Old journals did not record gaps. A difference in their totals also indicates missing observations.
        internal bool IsPartial => HasSample && (ObservationGap || !Dawn.HasValue || !Now.HasValue ||
            (long)Now.Value - Dawn.Value != Earned - Spent);

        internal bool SameResource(string asset)
        {
            return string.IsNullOrEmpty(Asset) || Asset == asset;
        }

        internal bool Observe(int? amount)
        {
            if (Now == amount) return false;
            if (Now.HasValue && !amount.HasValue) ObservationGap = true;
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
        [DataMember(EmitDefaultValue = false)] internal string Attempt;

        // Separate rows allow the same numeric slot to be reused by a different asset mid-day.
        [DataMember(EmitDefaultValue = false)] internal List<KeyValuePair<int, Balance>> Removed;

        internal IEnumerable<KeyValuePair<int, Balance>> AllRows()
        {
            foreach (var row in Rows) yield return row;
            if (Removed != null)
                foreach (var row in Removed) yield return row;
        }
    }

    internal sealed class Journal
    {
        private readonly string directory;
        private string current;
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

        internal bool Browse(ref string[] pages, ref int index, ref DayRecord selected, int target, Action<Exception> report)
        {
            string selectedPath = index >= 0 ? pages[index] : null;
            var step = target > index ? 1 : -1;
            HashSet<string> unreadable = null;
            bool opened = false;
            for (; target >= 0 && target < pages.Length; target += step)
            {
                string path = pages[target];
                DayRecord record = null;
                try { record = Read(path); }
                catch (Exception e) { report(e); }
                if (record != null)
                {
                    selected = record;
                    selectedPath = path;
                    opened = true;
                    break;
                }
                (unreadable ??= new HashSet<string>()).Add(path);
            }

            if (unreadable != null) pages = Array.FindAll(pages, path => !unreadable.Contains(path));
            index = selectedPath == null ? -1 : Array.IndexOf(pages, selectedPath);
            return opened || unreadable != null;
        }

        internal static string Fingerprint(string json)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
        }

        internal static string CheckpointKey(string nativeHash, string payload)
        {
            if (!string.IsNullOrEmpty(nativeHash)) return Fingerprint(nativeHash);
            return string.IsNullOrEmpty(payload) ? null : "payload-" + Fingerprint(payload);
        }

        private string Checkpoint(string fingerprint) => Path.Combine(directory, "save-" + fingerprint + ".json");

        internal void SaveCheckpoint(string fingerprint, DayRecord record)
        {
            string attempt = record.Attempt;
            record.Attempt = Path.GetFileName(current);
            try { Write(Checkpoint(fingerprint), record); }
            finally { record.Attempt = attempt; }
        }

        internal DayRecord LoadCheckpoint(string fingerprint) => Read(Checkpoint(fingerprint));

        private string AttemptPath(DayRecord checkpoint) =>
            checkpoint?.Attempt != null && Regex.IsMatch(checkpoint.Attempt, @"\Aday-[0-9]{19}-[0-9a-f]{32}\.json\z")
                ? Path.Combine(directory, checkpoint.Attempt) : null;

        // Reuse an unchanged attempt when Continue resumes the same checkpoint and day.
        // A replay from an older checkpoint still has its own observational history.
        internal void ResumeAttempt(DayRecord checkpoint, int day)
        {
            string path = AttemptPath(checkpoint);
            if (path != null && checkpoint.Day == day && MatchesAttempt(path, checkpoint))
                current = path;
        }

        // The tavern commits the next morning. Archive the exact saved day in its original page.
        internal void FinalizeAttempt(string fingerprint, int day)
        {
            var checkpoint = LoadCheckpoint(fingerprint);
            string path = AttemptPath(checkpoint);
            if (path == null || checkpoint.Day != day || MatchesAttempt(path, checkpoint)) return;
            checkpoint.Attempt = null;
            Write(path, checkpoint);
        }

        private bool MatchesAttempt(string path, DayRecord checkpoint)
        {
            if (!File.Exists(path)) return false;
            string attempt = checkpoint.Attempt;
            using var stream = new MemoryStream();
            checkpoint.Attempt = null;
            try { format.WriteObject(stream, checkpoint); }
            finally { checkpoint.Attempt = attempt; }
            return stream.ToArray().SequenceEqual(File.ReadAllBytes(path));
        }

        internal DayRecord Read(string path)
        {
            if (!File.Exists(path)) return null;
            using (var stream = File.OpenRead(path))
            {
                var data = format.ReadObject(stream) as DayRecord;
                if (data == null || data.Version != 1 || data.Rows == null || data.Rows.Count == 0 ||
                    data.AllRows().Any(row => row.Value == null || row.Value.Earned < 0 || row.Value.Spent < 0))
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
