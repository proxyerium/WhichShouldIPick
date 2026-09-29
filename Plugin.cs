using BepInEx;

namespace WhichShouldIPick
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "proxyerium.which-should-i-pick";
        public const string PluginName = "Which should I pick?";
        public const string PluginVersion = "0.1";

        private void Awake()
        {
            Logging.Apply(Logger);
            Hook.Apply();
        }
    }
}
