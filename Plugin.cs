using BepInEx;

namespace WhichShouldIPick
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "proxyerium.which-should-i-pick";
        public const string PluginName = "Which should I pick?";
        public const string PluginVersion = "0.2";

        private void Awake()
        {
            Keybinds.Register();
            Hook.Apply();
            Options.Register();
        }
    }
}
