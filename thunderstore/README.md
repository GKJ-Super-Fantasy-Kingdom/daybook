Daybook adds a daily balance beneath your kingdom’s resource bar. If you started the day with 3 boards and have 1 left, a red **−2** appears directly under the boards icon. You can see what you’ve gained or spent while keeping the map in view.

![Human Kingdom resource strip showing a gain of three boards](https://raw.githubusercontent.com/GKJ-Super-Fantasy-Kingdom/Daybook/main/docs/screenshots/01-human-compact.png)

## Hold Alt to open the ledger

The compact strip follows the same order as the material icons above it. Green numbers show a gain, red numbers show a loss, and zero means your stock is back at its dawn balance.

Hold **Alt** to see the full ledger. **Dawn** is the amount you had that morning, **Earned** and **Spent** count increases and decreases Daybook observes in available stock, **Now** is what you have on hand, and **Net** is the difference from dawn. Earning 8 boards and spending all 8 shows 8 earned, 8 spent, and 0 net. Release Alt to return to the compact strip.

| Resource group | What you can check |
| --- | --- |
| Materials | Wheat, wood, boards, stone, gold, and ingots in the Human Kingdom. Bone, stone, block, ooze, blood, and book in the Undead Kingdom. |
| Food and supplies | Individual stocks such as berries, bread, fish, beer, flour, and water, or kingdom-specific supplies such as candy, potions, souls, and paper. |
| Currency | Coins and faith. |
| Progression | Shards, stars, and super metal. |

![Earlier Undead Kingdom ledger at 1.5× UI scale](https://raw.githubusercontent.com/GKJ-Super-Fantasy-Kingdom/Daybook/main/docs/screenshots/04-undead-ledger-scale-1.5.png)

## Continue a saved kingdom

Hold **Alt** and press **Page Up** to browse earlier records from the same run. **Page Down** moves toward today. Release Alt to return to the live resource strip.

Daybook saves locally as you play, including when the overlay is hidden. Continuing from a matching checkpoint restores that day's balances. If the game restarts at an earlier dawn, you can read your previous attempt in history while the live ledger tracks the replay. Earlier versions have no saved history to import.

## Install and play

Choose **Install with App** on this page, select your Super Fantasy Kingdom profile, and launch with **Start modded**. The strip appears beneath the material icons when you enter a kingdom.

To hide Daybook, set `ShowOverlay = false` in your profile’s `BepInEx/config/sfk.daybook.cfg`.

The 0.3.0 layout was tested on Windows with **Super Fantasy Kingdom 0.8.067** and **BepInEx 5.4.23.5** in both Human and Undead kingdoms. The expanded Undead ledger was checked at **1920 × 1080** with UI scales **1** and **1.5**.
