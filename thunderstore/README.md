# Daybook

Daybook shows what your kingdom earned, what it spent, and how much is left each day. Check six materials at a glance beneath the resource bar, or open the ledger to compare today's supplies with earlier days in the same run.

## Features

- **Separate gains and spending.** If you gain 8 boards and spend 8 on buildings, the ledger shows 8 earned, 8 spent, and 0 net. You can see the activity even when your stock ends where it started.
- **Follow your kingdom's resources.** The full ledger covers materials, food and supplies, currency, and progression. Human kingdoms can track wood and bread, while Undead kingdoms can track bones and souls.
- **Keep a daily history.** Daybook saves records locally for each profile, kingdom, and run. Continue restores the ledger for a matching checkpoint. If you replay a dawn, the earlier attempt stays available to browse.

## How to Use

1. After the first dawn Daybook observes, look beneath each material icon for its net change since dawn.
2. Hold **Alt** on keyboard, **RT** on Xbox, or **R2** on PlayStation to open the ledger. Read across a row to compare **Dawn**, **Earned**, **Spent**, **Now**, and **Net**. On a saved day, **Last** shows the stock at the last observation for that record. An asterisk marks a resource observed for only part of the day.
3. Keep holding the detail key or trigger. Press **Page Up** (keyboard), **LB** (Xbox), or **L1** (PlayStation) for an older record. Press **Page Down**, **RB**, or **R1** to move toward today. The footer shows which directions are available and follows your controller bindings.
4. Release **Alt**, **RT**, or **R2** to return directly to the live strip. Today's totals keep updating while you browse.

## Screenshots

![Six daily material balances beneath the resource bar at 1×](https://raw.githubusercontent.com/GKJ-Super-Fantasy-Kingdom/daybook/v0.4.2/docs/screenshots/0.4.2/compact-1x.png)

*Read down from each material icon to see its net change since dawn.*

![Live Day 10 ledger beside the pinned Lumberyard upgrade at 1×](https://raw.githubusercontent.com/GKJ-Super-Fantasy-Kingdom/daybook/v0.4.2/docs/screenshots/0.4.2/live-1x.png)

*Wood started at 1. After spending it, Now reads 0 and Net reads −1.*

![Saved Day 10 ledger with Older and Newer bumper prompts at 1.5×](https://raw.githubusercontent.com/GKJ-Super-Fantasy-Kingdom/daybook/v0.4.2/docs/screenshots/0.4.2/saved-1p5x.png)

*On this saved page, wood shows 2 earned and 1 spent. Last reads 2 and Net reads +1. The footer shows both paging directions.*

Captured in Daybook 0.4.2 on Super Fantasy Kingdom 0.8.068 at 1920 × 1080 with a virtual Xbox-style controller.

## Compatibility

- The ledger follows both Human and Undead resource bars at UI scales 1× and 1.5×.
- On Windows game build 0.8.068, the strip and ledger cleared the [Pin a Build Cost](https://thunderstore.io/c/super-fantasy-kingdom/p/GKJ/PinABuildCost/) upgrade, [Kingdom Charters](https://thunderstore.io/c/super-fantasy-kingdom/p/GKJ/KingdomCharters/) debt badge, and [Visitor Calendar](https://thunderstore.io/c/super-fantasy-kingdom/p/GKJ/VisitorCalendar/) prompt at both scales with [Production Bottlenecks](https://thunderstore.io/c/super-fantasy-kingdom/p/GKJ/ProductionBottlenecks/) enabled.
- With ILM's [CheatMenu](https://thunderstore.io/c/super-fantasy-kingdom/p/ILM/CheatMenu/) 1.0.3 set to triple coin rewards, Daybook counted a 5-coin reward as 15 earned and a subsequent 2-coin purchase as 2 spent.
- If you use ownlyme's [Tavern Food Priorities](https://thunderstore.io/c/super-fantasy-kingdom/p/ownlyme/TavernFoodPriorities/), the food rows show kingdom stock. Meals eaten from the tavern's private stores are outside those totals.
- If a mod silently adds and removes stock between observations, the **Earned** and **Spent** columns can miss those changes.

## Installation

1. Choose **Install with App** and select your Super Fantasy Kingdom profile in Thunderstore Mod Manager or r2modman.
2. Install Daybook with its listed dependencies, [BepInExPack](https://thunderstore.io/c/super-fantasy-kingdom/p/BepInEx/BepInExPack/) and [HideManagerGameObject_Config](https://thunderstore.io/c/super-fantasy-kingdom/p/BepInEx/HideManagerGameObject_Config/).
3. Choose **Start modded** and enter your kingdom. To keep recording with the HUD hidden, set `ShowOverlay = false` in your profile's `BepInEx/config/sfk.daybook.cfg`.

## Support

[Support GKJ Games on Ko-fi](https://ko-fi.com/gkj_games)
