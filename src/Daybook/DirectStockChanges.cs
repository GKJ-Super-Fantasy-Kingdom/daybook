using System;
using HarmonyLib;
using SuperFantasyKingdom;

namespace Daybook
{
    // These native setters change available stock without raising OnResourceAmountChanged.
    internal static class DirectStockChanges
    {
        internal static event Action<ResourceType> Changed;

        internal static void Install(Harmony harmony)
        {
            void Watch(Type manager, string method, Action observed)
            {
                var target = AccessTools.Method(manager, method, new[] { typeof(int) });
                harmony.Patch(target, postfix: new HarmonyMethod(observed.Method));
            }

            var resources = typeof(ResourceManager);
            Watch(resources, nameof(ResourceManager.SetCoins), Coins);
            Watch(resources, nameof(ResourceManager.AddToCoins), Coins);
            Watch(resources, nameof(ResourceManager.SetShards), Shards);
            Watch(resources, nameof(ResourceManager.AddToShards), Shards);
            Watch(resources, nameof(ResourceManager.SetStars), Stars);
            Watch(resources, nameof(ResourceManager.AddToStars), Stars);
            Watch(resources, nameof(ResourceManager.SetSuperMetal), SuperMetal);
            Watch(resources, nameof(ResourceManager.AddToSuperMetal), SuperMetal);
            Watch(typeof(FaithManager), nameof(FaithManager.SetFaith), Faith);
        }

        private static void Coins() => Changed?.Invoke(ResourceType.Coins);
        private static void Shards() => Changed?.Invoke(ResourceType.Shards);
        private static void Stars() => Changed?.Invoke(ResourceType.Stars);
        private static void SuperMetal() => Changed?.Invoke(ResourceType.SuperMetal);
        private static void Faith() => Changed?.Invoke(ResourceType.Faith);
    }
}
