using System;

namespace WhichShouldIPick
{
    // What the weapon key goes for when several weapons are in reach.
    //
    // The member names are what the config file stores, so renaming one would
    // reset the setting for everyone who already has it. What the menu shows
    // comes from Translate(WeaponPreference<member>) - see GetEnumDesc below.
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

    // What the food key goes for when several kinds of food are in reach.
    //
    // Member names are the config file's storage, so renaming one resets the
    // setting for players who already have it; the menu label comes from
    // Translate(FoodPreference<member>) through the same GetEnumDesc hook below.
    public enum FoodPreference
    {
        // Vanilla's own ordering: nearest first, flip bias.
        Default,

        // A corpse outranks anything else edible: meat, for a slugcat that eats
        // it - not the vegetable half of the food key's reach.
        Carnivorous,

        // Whatever is not an animal comes first: fruit, mushrooms, bubble
        // fruit, the things a slugcat eats whole rather than chews off a body.
        Vegetarian,
    }

    // The mod's Remix menu, which the game generates: binding a configurable is
    // all it takes, so there is no OptionInterface here and no element is
    // placed by hand.
    //
    // Every string the player sees is a neutral key of this mod's own
    // Text/Text_<lang>/strings.txt:
    //   WeaponPreference          - the option's label, which the menu translates
    //                           for itself out of the bind key
    //   WeaponPreferenceDesc      - the description
    //   WeaponPreferenceTab       - the tab's name
    //   WeaponPreference<member>  - the three value labels
    //   FoodPreference            - the second preference's label
    //   FoodPreferenceDesc        - its description
    //   FoodPreferenceTab         - its tab's name
    //   FoodPreference<member>    - its three value labels
    //   GrabConsumedCreatures     - the corpse option's label
    //   GrabConsumedCreaturesDesc - its description
    //   GeneralTab                  - the general tab's name
    //   VanillaGrabMeddling         - the last option's label
    //   VanillaGrabMeddlingDesc     - its description
    //
    // Two of those the game cannot reach on its own. A tab's name is printed
    // verbatim, so it is translated here at bind time. A value label is built
    // by EnumHelper.GetEnumDesc, which reads the [Description] attribute - a
    // compile-time constant with no way to be translated - so GetEnumDesc is
    // hooked below and the neutral key substituted there instead.
    //
    // The menu does not show tabs in bind order. InternalOI_Auto groups the
    // binds by their autoTab, then sorts the tab names with
    // ConfigContainer.comInfo.Compare(..., CompareOptions.StringSort) - the
    // invariant culture, so case-sensitive and by Unicode code point - and
    // sorts each tab's options by their config key. Nothing in the mod can fix
    // a tab's position, which is why the three tab names here were chosen so
    // that their translations sort the way the menu should read.
    internal static class Options
    {
        // Valid bind keys are letters, digits and underscores only.
        public const string GeneralTabKey = "GeneralTab";
        public const string VanillaMeddlingKey = "VanillaGrabMeddling";
        public const string VanillaMeddlingDescKey = "VanillaGrabMeddlingDesc";
        public const string WeaponTabKey = "WeaponPreferenceTab";
        public const string WeaponPreferenceKey = "WeaponPreference";
        public const string WeaponPreferenceDescKey = "WeaponPreferenceDesc";
        public const string FoodPreferenceKey = "FoodPreference";
        public const string FoodPreferenceDescKey = "FoodPreferenceDesc";
        public const string FoodTabKey = "FoodPreferenceTab";
        public const string ConsumedKey = "GrabConsumedCreatures";
        public const string ConsumedDescKey = "GrabConsumedCreaturesDesc";

        public static Configurable<WeaponPreference> WeaponPreference { get; private set; } = null!;

        // Which edible to reach for first. Default leaves the vanilla search
        // alone, so nothing changes for a player who never opens this menu.
        public static Configurable<FoodPreference> FoodPreference { get; private set; } = null!;

        // Off by default: a corpse with meat still on it is food for a slugcat
        // that could eat it, and the food key drags it. On, a corpse whose meat
        // is entirely gone - meatLeft down to 0, nothing left worth a bite -
        // stays food too instead of falling back to the vanilla key. A corpse
        // the slugcat cannot eat is never food either way.
        public static Configurable<bool> GrabConsumedCreatures { get; private set; } = null!;

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

            if (WeaponPreference != null)
            {
                return;
            }

            // A tab of its own: it is the mod's own switch rather than a rule
            // about what a key reaches for, so it has nothing to share with
            // the two preferences. Which tab a bind lands on is all autoTab
            // decides; the menu then orders the tabs itself, by sorting their
            // translated names - see the note above Options.
            VanillaMeddling = MachineConnector.GetRegisteredOI("which-should-i-pick").config.Bind(
                VanillaMeddlingKey,
                false,
                new ConfigurableInfo(
                    OptionInterface.Translate(VanillaMeddlingDescKey),
                    autoTab: OptionInterface.Translate(GeneralTabKey)));

            // Second tab. RainWorld.Awake has already set Custom.rainWorld, and
            // Translate loads the string tables itself on first use, so
            // translating here is safe - and necessary for the tab name, which
            // the menu prints exactly as given.
            WeaponPreference = MachineConnector.GetRegisteredOI("which-should-i-pick").config.Bind(
                WeaponPreferenceKey,
                global::WhichShouldIPick.WeaponPreference.Default,
                new ConfigurableInfo(
                    OptionInterface.Translate(WeaponPreferenceDescKey),
                    autoTab: OptionInterface.Translate(WeaponTabKey)));

            // Third, and last of the three: the two preferences hold one enum
            // each and read better apart. Its sibling, the corpse switch,
            // shares this page.
            FoodPreference = MachineConnector.GetRegisteredOI("which-should-i-pick").config.Bind(
                FoodPreferenceKey,
                global::WhichShouldIPick.FoodPreference.Default,
                new ConfigurableInfo(
                    OptionInterface.Translate(FoodPreferenceDescKey),
                    autoTab: OptionInterface.Translate(FoodTabKey)));

            // Same tab as the food preference: it is the same subject, whether
            // a corpse counts as food at all.
            GrabConsumedCreatures = MachineConnector.GetRegisteredOI("which-should-i-pick").config.Bind(
                ConsumedKey,
                false,
                new ConfigurableInfo(
                    OptionInterface.Translate(ConsumedDescKey),
                    autoTab: OptionInterface.Translate(FoodTabKey)));
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
                string key = WeaponPreferenceKey + value;
                string translated = OptionInterface.Translate(key);
                if (translated != key)
                {
                    return translated;
                }
            }

            if (value is FoodPreference)
            {
                string key = FoodPreferenceKey + value;
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
