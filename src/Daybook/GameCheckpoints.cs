using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.Tavern;
using UnityEngine;

namespace Daybook
{
    internal static class GameCheckpoints
    {
        internal static DaybookPlugin Plugin;
        private static readonly ConditionalWeakTable<GameData, CheckpointRead> Selected =
            new ConditionalWeakTable<GameData, CheckpointRead>();
        [ThreadStatic] private static ReadScope activeRead;

        [Serializable]
        private sealed class SaveIdentity
        {
#pragma warning disable CS0649 // Populated by Unity's JSON deserializer.
            public string runID;
            public string hash;
            public int day;
            public bool isNight;
#pragma warning restore CS0649
        }

        private sealed class ReadScope
        {
            internal string Path, Payload;
        }

        private sealed class CheckpointRead
        {
            internal string Path, Run, Key;
            internal int Day;
            internal bool Night;
        }

        private static bool IsCheckpoint(string path) =>
            path != null && path.EndsWith("_game", StringComparison.Ordinal);

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadGameData),
            typeof(string), typeof(bool), typeof(bool), typeof(bool))]
        internal static class SelectRead
        {
            private static void Prefix(string __0, out ReadScope __state)
            {
                __state = activeRead;
                activeRead = IsCheckpoint(__0) ? new ReadScope { Path = __0 } : null;
            }

            private static void Postfix(GameData __result)
            {
                if (__result == null || activeRead == null) return;
                string key = Journal.CheckpointKey(__result.hash, activeRead.Payload);
                if (key == null) return;
                Selected.Remove(__result);
                Selected.Add(__result, new CheckpointRead
                {
                    Path = activeRead.Path, Run = __result.runID, Key = key,
                    Day = __result.day, Night = __result.isNight
                });
            }

            private static void Finalizer(ReadScope __state) => activeRead = __state;
        }

        [HarmonyPatch]
        internal static class ReadPayload
        {
            private static MethodBase TargetMethod() =>
                typeof(SaveManager).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Single(method => method.Name == "Load" && !method.IsGenericMethod && method.ReturnType == typeof(string) &&
                        method.GetParameters().Select(parameter => parameter.ParameterType)
                            .SequenceEqual(new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) }));

            // The generic native loader calls this once for the primary and again if parsing falls back to backup.
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(string __0, string __result)
            {
                if (activeRead != null && activeRead.Path == __0) activeRead.Payload = __result;
            }
        }

        internal static string SelectedKey(GameData data) =>
            data == null ? null
            : Selected.TryGetValue(data, out var selection) ? selection.Key : Journal.CheckpointKey(data.hash, null);

        [HarmonyPatch(typeof(GameDataManager), "Load")]
        internal static class Load
        {
            // The game clears this field during Load, after it selects the actual save or backup.
            private static void Prefix(GameData ___m_GameData)
            {
                if (Plugin != null) Plugin.LoadedCheckpoint = SelectedKey(___m_GameData);
            }
        }

        [HarmonyPatch]
        internal static class Save
        {
            private static MethodBase TargetMethod()
            {
                var manager = typeof(SaveManager);
                var method = AccessTools.DeclaredMethod(manager, nameof(SaveManager.Save),
                    new[] { typeof(string), typeof(string), typeof(bool) })
                    ?? AccessTools.DeclaredMethod(manager, nameof(SaveManager.Save),
                        new[] { typeof(string), typeof(string) });
                if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                    throw new MissingMethodException("No supported SaveManager.Save checkpoint overload was found.");
                return method;
            }

            private static void Postfix(string __0, string __1)
            {
                if (Plugin == null || !IsCheckpoint(__0)) return;
                try
                {
                    var save = JsonUtility.FromJson<SaveIdentity>(__1);
                    if (save == null) return;
                    var tavern = TavernSaveManager.Instance;
                    var original = !save.isNight && tavern != null ? tavern.GetGameData() : null;
                    // The morning save reuses the GameData that the tavern loaded, with a new day and hash.
                    if (original != null && Selected.TryGetValue(original, out var night) && night.Night &&
                        night.Path == __0 && night.Run == save.runID && save.day == (long)night.Day + 1)
                        Plugin.FinalizeCheckpoint(save.runID, night.Key, save.day);
                    else
                        Plugin.Checkpoint(save.runID, Journal.CheckpointKey(save.hash, __1));
                }
                catch (Exception e)
                {
                    Plugin.ReportJournalError(e);
                }
            }
        }
    }
}
