using ImprovedInput;
using UnityEngine;

namespace WhichShouldIPick
{
    // Keybinds shown in the Remix input menu. No default binding on purpose:
    // players opt in through the input menu, so nothing is stolen from vanilla.
    internal static class Keybinds
    {
        public static PlayerKeybind GrabWeapon { get; private set; } = null!;
        public static PlayerKeybind GrabFood { get; private set; } = null!;

        // Called from Plugin.Awake, before any hook can run. The registrations
        // must not live in a static field initializer: the class is only reached
        // from inside hooks, so its initializer would run after the Remix input
        // menu is built and the keybinds would never show up there.
        public static void Register()
        {
            GrabWeapon = PlayerKeybind.Register(
                "which-should-i-pick:grab-weapon",
                "Which should I pick?",
                "Grab weapon",
                KeyCode.None,
                KeyCode.None);

            GrabFood = PlayerKeybind.Register(
                "which-should-i-pick:grab-food",
                "Which should I pick?",
                "Grab food",
                KeyCode.None,
                KeyCode.None);
        }
    }
}
