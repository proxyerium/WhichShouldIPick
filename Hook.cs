using System.Collections.Generic;
using ImprovedInput;
using RWCustom;
using UnityEngine;

namespace WhichShouldIPick
{
    // Category-filtered grab. The mod adds two keys that ask for one kind of
    // item; everything else is vanilla code running on vanilla state.
    //
    // Nothing here reflects on the game, and nothing here touches a member
    // that is private in the shipped assembly: the publicized reference may
    // declare such a member public, but the runtime still enforces the real
    // accessibility, so reading one throws FieldAccessException and Rain World
    // halts on the spot (which looks like a freeze, not a crash).
    //
    // Telling the mod's keys apart from the vanilla one takes two reads and no
    // state of our own: which of our keybinds was pressed a moment ago is
    // already kept by ImprovedInput, and whether *some* grab press is pending
    // is the game's own pckp bit inside vanilla's five-frame pickup window.
    // Only the first question needs the keybind identity - the second is
    // answered by the game, so the vanilla button's own keybind is never
    // consulted (how ImprovedInput reports a vanilla keybind is its business,
    // and a wrong answer there used to make the vanilla-grab option a no-op).
    internal static class Hook
    {
        // Vanilla arms a pickup with wantToPickUp = 5 on the press edge, and
        // that window outlives the key release, so a category key that was just
        // let go still has to be recognized for this many frames.

        public static void Apply()
        {
            // 1) Feed our keys into the game's own grab channel. checkInput is
            // the one place where the game turns buttons into an InputPackage,
            // and it shifts input[1] = input[0] first, so OR-ing the grab bit in
            // after orig hands GrabUpdate exactly the pckp signal the vanilla
            // Grab key would have produced - press edge, hold, and all of it.
            // Everything downstream (reach animation, hold to eat, double-tap
            // swap, and their timing) then runs as untouched vanilla code.
            // With the vanilla Grab button already down the bit is true anyway,
            // so this only ever adds a press the game did not have.
            On.Player.checkInput += (orig, self) =>
            {
                orig(self);

                if (self.input == null || self.input.Length == 0)
                {
                    return;
                }

                if (!self.IsPressed(Keybinds.GrabWeapon) && !self.IsPressed(Keybinds.GrabFood))
                {
                    return;
                }

                // InputPackage is a struct: mutate a copy, then write it back.
                var input = self.input[0];
                input.pckp = true;
                self.input[0] = input;
            };

            // 2) The reach animation and its straight-to-back shortcut ask for
            // one item through PickupCandidate. A category key answers with
            // that kind only; nothing of it in range -> null, which vanilla
            // handles by doing the reach and grabbing nothing. With the option
            // on, a plain vanilla grab answers with everything except the two
            // kinds the mod's keys own.
            On.Player.PickupCandidate += (orig, self, favorSpears) =>
            {
                if (RequestedCategory(self, out GrabFilter filter))
                {
                    return FindBestInCategory(self, favorSpears, filter);
                }

                return VanillaMeddlingOn() && GrabPending(self)
                    ? FindBestInCategory(self, favorSpears, GrabFilter.Other)
                    : orig(self, favorSpears);
            };

            // 3) Vanilla also grabs without ever asking for a candidate:
            // Player.Collide picks up whatever the body just touched while a
            // pickup is pending, and that path never consults the candidate.
            // Every grab funnels through SlugcatGrab, so the active filter is
            // enforced here for as long as the request lasts - letting go of
            // the key is not the end of it.
            //
            // "As long as the request lasts" is measured by RequestedCategory's
            // own look-back, which is the same window (vanilla arms a pickup for
            // 5 frames on the press edge), rather than by reading
            // Player.wantToPickUp: that field is private in the shipped
            // assembly, so reading it - through the publicized reference, which
            // pretends it is public - throws FieldAccessException at runtime and
            // Rain World stops dead on the spot.
            On.Player.SlugcatGrab += (orig, self, obj, graspUsed) =>
            {
                if (obj != null && !Allowed(self, obj))
                {
                    return;
                }

                orig(self, obj, graspUsed);
            };

            // 4) Down + Grab is vanilla's "put it down", and it lands in
            // Player.ReleaseObject: GrabUpdate picks the first occupied hand
            // and lays that item on the ground. A category key has to mean the
            // same thing when putting down as when picking up - only an item
            // of the asked-for kind goes down. When the hand vanilla picked
            // holds something else, the matching hand goes down instead;
            // nothing of that kind in hand means the press drops nothing.
            On.Player.ReleaseObject += (orig, self, grasp, eu) =>
            {
                if (!RequestedCategory(self, out GrabFilter filter)
                    || MatchesFilter(Grabbed(self, grasp), filter))
                {
                    orig(self, grasp, eu);
                    return;
                }

                for (int i = 0; i < self.grasps.Length; i++)
                {
                    if (MatchesFilter(Grabbed(self, i), filter))
                    {
                        orig(self, i, eu);
                        return;
                    }
                }
            };
        }

        // What a grasp currently holds, or null for an empty hand. Hand
        // indices run 0..1, and ReleaseObject is only ever reached with a
        // non-null hand, but the search below probes both.
        private static PhysicalObject? Grabbed(Player player, int grasp)
        {
            return player.grasps[grasp]?.grabbed;
        }

        // The vanilla Grab key is never identified by its keybind. What the mod
        // needs to know is whether *some* grab press is still pending, and the
        // game already keeps exactly that: pckp across the five passes that
        // make up vanilla's pickup window (Player.PickupPressed arms
        // wantToPickUp = 5 on the press edge, and holding does not re-arm it,
        // so this window is the same one vanilla itself acts in). A bit the mod
        // injected for one of its own keys is indistinguishable from the button
        // here - and does not have to be, because RequestedCategory is asked
        // first everywhere and answers the 'my key' half on its own.
        private static bool GrabPending(Player player)
        {
            int frames = System.Math.Min(player.input.Length, 5);
            for (int age = 0; age < frames; age++)
            {
                if (player.input[age].pckp)
                {
                    return true;
                }
            }

            return false;
        }

        // Which category is being asked for, if any. A key held right now wins;
        // otherwise the category whose press may still be inside vanilla's
        // pickup window. The look-back reads the input history ImprovedInput
        // already keeps, so a release needs no state of our own.
        private static bool RequestedCategory(Player player, out GrabFilter filter)
        {
            if (player.IsPressed(Keybinds.GrabWeapon))
            {
                filter = GrabFilter.Weapon;
                return true;
            }

            if (player.IsPressed(Keybinds.GrabFood))
            {
                filter = GrabFilter.Food;
                return true;
            }

            CustomInput[] history = player.InputHistory();
            int frames = System.Math.Min(history.Length, 5);

            // Newest press first: that is the one that armed the window we are
            // still inside. Both keys in the same frame: weapon wins.
            for (int age = 0; age < frames; age++)
            {
                if (history[age][Keybinds.GrabWeapon])
                {
                    filter = GrabFilter.Weapon;
                    return true;
                }

                if (history[age][Keybinds.GrabFood])
                {
                    filter = GrabFilter.Food;
                    return true;
                }
            }

            filter = GrabFilter.Other;
            return false;
        }

        // Whether this grab is one the mod lets through. Three states, never
        // two: one of the mod's keys narrows the grab to its own kind, a plain
        // vanilla grab answers with vanilla's whole search - or, with the
        // option on, with the two kinds the mod's keys own taken out of it -
        // and a grab with no pending press behind it (an item handed over by
        // another creature, a scripted pickup) is left alone entirely.
        private static bool Allowed(Player player, PhysicalObject obj)
        {
            if (RequestedCategory(player, out GrabFilter filter))
            {
                return MatchesFilter(obj, filter);
            }

            if (!VanillaMeddlingOn() || !GrabPending(player))
            {
                return true;
            }

            return MatchesFilter(obj, GrabFilter.Other);
        }

        // The mod's own option, read without trusting that the interface
        // registered: a throw from inside a hook freezes the game, so a missing
        // binding has to read as 'off' rather than blow up.
        private static bool VanillaMeddlingOn()
        {
            return Options.VanillaMeddling?.Value ?? false;
        }

        // What a grab can be narrowed to. Weapon and Food are what the mod's
        // two keys ask for; Other is vanilla's search with both of them
        // removed, which is what the vanilla key becomes when the player turns
        // the option on.
        private enum GrabFilter
        {
            Weapon,
            Food,
            Other,
        }

        // Both categories are the game's own notions, so modded items follow
        // along without a list: a weapon is what the game calls a Weapon, food
        // is what the game currently calls edible.
        private static bool MatchesFilter(PhysicalObject? obj, GrabFilter filter)
        {
            if (filter == GrabFilter.Weapon)
            {
                return obj is Weapon;
            }

            if (filter == GrabFilter.Food)
            {
                return obj is IPlayerEdible edible && edible.Edible;
            }

            return !(obj is Weapon) && !(obj is IPlayerEdible edible2 && edible2.Edible);
        }

        // The two kinds the preference can ask for, named by the game's own
        // types. Everything else the game calls a weapon - plain and electric
        // spears, boomerangs, bullets - is deliberately in neither list, so it
        // only ever comes up when nothing preferred is in reach.
        private static bool MatchesPreference(PhysicalObject obj, WeaponPreference prefer)
        {
            if (prefer == WeaponPreference.Explosives)
            {
                return obj is ScavengerBomb
                    || obj is ExplosiveSpear
                    || obj is FirecrackerPlant
                    || obj is MoreSlugcats.SingularityBomb;
            }

            return obj is Rock
                || obj is FlareBomb
                || obj is PuffBall
                || obj is SporePlant
                || obj is GraffitiBomb
                || obj is MoreSlugcats.LillyPuck;
        }

        // Vanilla's own candidate search - Player.PickupCandidate - with the one
        // condition it cannot take: only this category counts. Same reach rules,
        // same spear favor, same flip bias, so what gets picked is what vanilla
        // would have picked from a room holding nothing but this category.
        private static PhysicalObject? FindBestInCategory(Player player, float favorSpears, GrabFilter filter)
        {
            Room? room = player.room;
            if (room == null)
            {
                return null;
            }

            Vector2 pos = player.bodyChunks[0].pos;
            int playerRipple = player.abstractPhysicalObject.rippleLayer;
            bool playerBothSides = player.abstractPhysicalObject.rippleBothSides;

            // A preference only reorders what vanilla would already have
            // considered: it cannot invent reach, and it is a weapon option, so
            // it has nothing to say about the other two filters. The preferred
            // kind is kept as a second best, so vanilla's own order still
            // decides among the preferred items and, when the room holds none
            // of them, the answer is exactly the one vanilla would have given.
            // Nullable on purpose: the only way this is unset is an interface
            // that never registered, and a throw from inside this hook would
            // freeze the game rather than report the problem.
            WeaponPreference prefer = filter == GrabFilter.Weapon
                ? Options.WeaponPrefer?.Value ?? WeaponPreference.Default
                : WeaponPreference.Default;
            PhysicalObject? preferred = null;
            float preferredScore = float.MaxValue;

            PhysicalObject? best = null;
            float bestScore = float.MaxValue;

            List<PhysicalObject>[] layers = room.physicalObjects;
            for (int i = 0; i < layers.Length; i++)
            {
                List<PhysicalObject> layer = layers[i];
                for (int j = 0; j < layer.Count; j++)
                {
                    PhysicalObject obj = layer[j];
                    if (!MatchesFilter(obj, filter))
                    {
                        continue;
                    }

                    AbstractPhysicalObject apo = obj.abstractPhysicalObject;
                    if (!(apo.rippleLayer == playerRipple || apo.rippleBothSides || playerBothSides))
                    {
                        continue;
                    }

                    if (obj is PlayerCarryableItem carryable && carryable.forbiddenToPlayer >= 1)
                    {
                        continue;
                    }

                    BodyChunk chunk = obj.bodyChunks[0];
                    if (!Custom.DistLess(pos, chunk.pos, chunk.rad + 40f))
                    {
                        continue;
                    }

                    if (!Custom.DistLess(pos, chunk.pos, chunk.rad + 20f) && !room.VisualContact(pos, chunk.pos))
                    {
                        continue;
                    }

                    if (!player.CanIPickThisUp(obj))
                    {
                        continue;
                    }

                    float score = Vector2.Distance(pos, chunk.pos);
                    if (obj is Spear)
                    {
                        score -= favorSpears;
                    }

                    if ((chunk.pos.x < pos.x) == (player.flipDirection < 0))
                    {
                        score -= 10f;
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = obj;
                    }

                    if (prefer != WeaponPreference.Default && score < preferredScore && MatchesPreference(obj, prefer))
                    {
                        preferredScore = score;
                        preferred = obj;
                    }
                }
            }

            return preferred ?? best;
        }
    }
}
