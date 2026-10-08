using System;

namespace WhichShouldIPick;

public enum WeaponPreference
{
    Default,
    Explosives,
    Utilities,
}

public enum FoodPreference
{
    Default,
    Carnivorous,
    Vegetarian,
}

internal static class Options
{
    private static bool _isInit;

    public static Configurable<WeaponPreference> WeaponPreference { get; private set; } = null!;
    public static Configurable<FoodPreference> FoodPreference { get; private set; } = null!;
    public static Configurable<bool> GrabConsumedCreatures { get; private set; } = null!;
    public static Configurable<bool> VanillaMeddling { get; private set; } = null!;

    public static void Register()
    {
        On.RainWorld.OnModsInit += OnModsInit;
        On.Menu.Remix.MixedUI.OpResourceSelector.GetEnumNames += GetEnumNames;
        On.Menu.Remix.MixedUI.EnumHelper.GetEnumDesc += GetEnumDesc;
    }

    private static void OnModsInit(On.RainWorld.orig_OnModsInit orig, RainWorld self)
    {
        orig(self);

        if (_isInit)
        {
            return;
        }

        _isInit = true;

        var config = MachineConnector.GetRegisteredOI("which-should-i-pick").config;

        VanillaMeddling = config.Bind(
            "VanillaGrabMeddling",
            false,
            new ConfigurableInfo(
                OptionInterface.Translate("VanillaGrabMeddlingDesc"),
                autoTab: OptionInterface.Translate("GeneralTab")));

        WeaponPreference = config.Bind(
            "WeaponPreference",
            WhichShouldIPick.WeaponPreference.Default,
            new ConfigurableInfo(
                OptionInterface.Translate("WeaponPreferenceDesc"),
                autoTab: OptionInterface.Translate("WeaponPreferenceTab")));

        FoodPreference = config.Bind(
            "FoodPreference",
            WhichShouldIPick.FoodPreference.Default,
            new ConfigurableInfo(
                OptionInterface.Translate("FoodPreferenceDesc"),
                autoTab: OptionInterface.Translate("FoodPreferenceTab")));

        GrabConsumedCreatures = config.Bind(
            "GrabConsumedCreatures",
            false,
            new ConfigurableInfo(
                OptionInterface.Translate("GrabConsumedCreaturesDesc"),
                autoTab: OptionInterface.Translate("FoodPreferenceTab")));
    }

    private static string GetEnumDesc(On.Menu.Remix.MixedUI.EnumHelper.orig_GetEnumDesc orig, Enum value)
    {
        if (value is WeaponPreference)
        {
            string key = "WeaponPreference" + value;
            string translated = OptionInterface.Translate(key);
            if (translated != key)
            {
                return translated;
            }
        }

        if (value is FoodPreference)
        {
            string key = "FoodPreference" + value;
            string translated = OptionInterface.Translate(key);
            if (translated != key)
            {
                return translated;
            }
        }

        return orig(value);
    }

    private static Menu.Remix.MixedUI.ListItem[] GetEnumNames(
        On.Menu.Remix.MixedUI.OpResourceSelector.orig_GetEnumNames orig,
        Menu.Remix.MixedUI.UIconfig caller,
        Type enumType)
    {
        Menu.Remix.MixedUI.ListItem[] items = orig(caller, enumType);

        string? prefix = null;
        if (enumType == typeof(WeaponPreference))
        {
            prefix = "WeaponPreference";
        }
        else if (enumType == typeof(FoodPreference))
        {
            prefix = "FoodPreference";
        }

        if (prefix == null)
        {
            return items;
        }

        for (int i = 0; i < items.Length; i++)
        {
            string key = prefix + items[i].name + "Desc";
            string translated = OptionInterface.Translate(key);
            if (translated != key)
            {
                items[i].desc = translated;
            }
        }

        return items;
    }
}
