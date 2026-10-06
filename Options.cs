using System;

namespace WhichShouldIPick
{
    // What the weapon key goes for when several weapons are in reach.
    //
    // The member names are what the config file stores, so renaming one would
    // reset the setting for everyone who already has it. What the menu shows
    // comes from Translate(WeaponPrefer<member>) - see GetEnumDesc below.
    public enum WeaponPreference
    {
        // Vanilla's own ordering: nearest first, spear favored, flip bias.
        Default,

        // Things that go off: scavenger bombs, explosive spears, the
        // singularity bomb and firecracker plants.
        Explosives,

        // Stun and support items: rocks, lilypucks, flares, puffballs, spore
        // plants and graffiti bombs.
        Utilities,
    }

    // The mod's Remix menu, which the game generates: binding a configurable is
    // all it takes, so there is no OptionInterface here and no element is
    // placed by hand.
    //
    // Every string the player sees is a neutral key of this mod's own
    // Text/Text_<lang>/strings.txt:
    //   WeaponPrefer          - the option's label, which the menu translates
    //                           for itself out of the bind key
    //   WeaponPreferDesc      - the description
    //   WeaponPreferTab       - the tab's name
    //   WeaponPrefer<member>  - the three value labels
    //   VanillaGrabMeddling         - the second option's label
    //   VanillaGrabMeddlingDesc     - its description
    //
    // Two of those the game cannot reach on its own. A tab's name is printed
    // verbatim, so it is translated here at bind time. A value label is built
    // by EnumHelper.GetEnumDesc, which reads the [Description] attribute - a
    // compile-time constant with no way to be translated - so GetEnumDesc is
    // hooked below and the neutral key substituted there instead.
    internal static class Options
    {
        // Valid bind keys are letters, digits and underscores only.
        public const string WeaponPreferKey = "WeaponPrefer";
        public const string WeaponPreferDescKey = "WeaponPreferDesc";
        public const string TabKey = "WeaponPreferTab";
        public const string VanillaMeddlingKey = "VanillaGrabMeddling";
        public const string VanillaMeddlingDescKey = "VanillaGrabMeddlingDesc";

        public static Configurable<WeaponPreference> WeaponPrefer { get; private set; } = null!;

        // Off by default: while it is off the vanilla Grab key behaves exactly
        // as vanilla does, which is what a player who never opens this menu
        // should get. On, it makes the two keys the mod adds the only way to
        // pick up a weapon or edible food - vanilla's own key walks past both.
        public static Configurable<bool> VanillaMeddling { get; private set; } = null!;

        // Called from Plugin.Awake, before the game starts loading mods.
        public static void Register()
        {
            On.RainWorld.OnModsInit += OnModsInit;
            On.Menu.Remix.MixedUI.EnumHelper.GetEnumDesc += GetEnumDesc;
        }

        // OnModsInit runs after the mod list is built and after each mod's config
        // file was read, so binding here both finds the mod's automatic option
        // interface and restores the saved value (Bind applies stray values the
        // load could not match). The event can fire more than once in a session.
        private static void OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
        {
            orig(self);

            if (WeaponPrefer != null)
            {
                return;
            }

            // RainWorld.Awake has already set Custom.rainWorld, and Translate
            // loads the string tables itself on first use, so translating here
            // is safe - and necessary for the tab name, which the menu prints
            // exactly as given.
            WeaponPrefer = MachineConnector.GetRegisteredOI("which-should-i-pick").config.Bind(
                WeaponPreferKey,
                WeaponPreference.Default,
                new ConfigurableInfo(
                    OptionInterface.Translate(WeaponPreferDescKey),
                    autoTab: OptionInterface.Translate(TabKey)));

            // Same tab as the preference, and the same object graph: one
            // automatic option interface holds both configurables, so binding
            // this second one costs no menu code.
            VanillaMeddling = MachineConnector.GetRegisteredOI("which-should-i-pick").config.Bind(
                VanillaMeddlingKey,
                false,
                new ConfigurableInfo(
                    OptionInterface.Translate(VanillaMeddlingDescKey),
                    autoTab: OptionInterface.Translate(TabKey)));
        }

        // The element showing an enum configurable fills its list from
        // Enum.GetNames and takes each label from this call, so this is the one
        // place where a value can be a neutral key. Only this mod's own enum is
        // touched, so no other menu changes, and an untranslated key falls
        // through to orig - which returns null for an enum without a
        // [Description] attribute, leaving the element to show the member name.
        private static string GetEnumDesc(On.Menu.Remix.MixedUI.EnumHelper.orig_GetEnumDesc orig, Enum value)
        {
            if (value is WeaponPreference)
            {
                string key = WeaponPreferKey + value;
                string translated = OptionInterface.Translate(key);
                if (translated != key)
                {
                    return translated;
                }
            }

            return orig(value);
        }
    }
}
