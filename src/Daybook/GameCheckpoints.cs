using System;
using HarmonyLib;
using SuperFantasyKingdom;
using UnityEngine;

namespace Daybook
{
    internal static class GameCheckpoints
    {
        internal static DaybookPlugin Plugin;

        [Serializable]
        private sealed class SaveIdentity
        {
#pragma warning disable CS0649 // Populated by Unity's JSON deserializer.
            public string runID;
            public string hash;
#pragma warning restore CS0649
        }

        [HarmonyPatch(typeof(GameDataManager), "Load")]
        internal static class Load
        {
            // The game clears this field during Load, after it selects the actual save or backup.
            private static void Prefix(GameData ___m_GameData)
            {
                if (Plugin != null) Plugin.LoadedHash = ___m_GameData?.hash;
            }
        }

        [HarmonyPatch(typeof(SaveManager), "Save", typeof(string), typeof(string))]
        internal static class Save
        {
            private static void Postfix(string __0, string __1)
            {
                if (Plugin == null || __0 == null || !__0.EndsWith("_game", StringComparison.Ordinal)) return;
                try
                {
                    var save = JsonUtility.FromJson<SaveIdentity>(__1);
                    if (save != null) Plugin.Checkpoint(save.runID, save.hash);
                }
                catch (Exception e)
                {
                    Plugin.ReportJournalError(e);
                }
            }
        }
    }
}
