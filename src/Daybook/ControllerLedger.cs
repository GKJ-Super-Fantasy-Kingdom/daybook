using HarmonyLib;
using SuperFantasyKingdom;
using SuperFantasyKingdom.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Daybook
{
    internal static class ControllerLedger
    {
        internal const string Details = "Game/WorkerMenu";
        internal const string Older = "Game/PauseToggle";
        internal const string Newer = "Game/ChangeGameSpeed";
        internal static DaybookPlugin Plugin;

        internal static PlayerInput ActivePlayer()
        {
            var manager = GamepadManager.Instance;
            if (manager == null || !manager.IsGamepadInUse()) return null;
            var player = manager.GetPlayerInput();
            return player != null && player.actions.FindAction(Details)?.enabled == true ? player : null;
        }

        internal static Sprite Glyph(PlayerInput player, string actionPath, out string bindingName)
        {
            var action = player.actions.FindAction(actionPath);
            int index = action?.GetBindingIndex(group: player.currentControlScheme) ?? -1;
            bindingName = index < 0 ? "" : action.GetBindingDisplayString(index);
            if (index < 0) return null;
            var binding = action.bindings[index];
            string key = binding.effectivePath;
            if (!string.IsNullOrEmpty(binding.effectiveProcessors)) key += "Processor";
            return GamepadManager.Instance.GetButtonPrompt(key, false);
        }

        private static bool Captures(InputAction.CallbackContext context)
        {
            return context.performed && context.control?.device is Gamepad && Plugin != null &&
                Plugin.CanPageWithController();
        }

        // Route the game's rebound actions at their handlers, before pause or speed takes effect.
        [HarmonyPatch(typeof(GameInputManager), nameof(GameInputManager.PauseToggle))]
        internal static class OlderAction
        {
            private static bool Prefix(InputAction.CallbackContext __0)
            {
                if (!Captures(__0)) return true;
                Plugin.PageHistory(1);
                return false;
            }
        }

        [HarmonyPatch(typeof(GameInputManager), nameof(GameInputManager.TimeButton))]
        internal static class NewerAction
        {
            private static bool Prefix(InputAction.CallbackContext __0)
            {
                if (!Captures(__0)) return true;
                Plugin.PageHistory(-1);
                return false;
            }
        }

        // Some HUD prompts also invoke their button directly on the same performed event.
        [HarmonyPatch(typeof(UIButtonPrompt), "OnActionPerformed")]
        internal static class HudAction
        {
            private static bool Prefix(InputAction.CallbackContext __0)
            {
                var action = __0.action;
                bool paging = action.actionMap?.name == "Game" &&
                    (action.name == "PauseToggle" || action.name == "ChangeGameSpeed");
                return !paging || !Captures(__0);
            }
        }
    }
}
