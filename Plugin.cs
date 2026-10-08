using BepInEx;

namespace WhichShouldIPick;

[BepInPlugin("proxyerium.which-should-i-pick", "Which should I pick?", "0.5")]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        Options.Register();
        Keybinds.Register();
        Hook.Apply();
    }
}
