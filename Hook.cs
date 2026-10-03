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
    // halts on the spot (which looks like a freeze, not a crash). The only
    // cross-frame fact we need - which key was pressed a moment ago - is
    // already kept by ImprovedInput, so the mod holds no state of its own.
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
            On.Player.checkInput += (orig, self) =>
            {
                orig(self);

                if (self.input == null || self.input.Length == 0 || VanillaGrab(self))
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
            // one item through PickupCandidate. While a category is asked for
            // the answer is that category only; nothing of it in range -> null,
            // which vanilla handles by doing the reach and grabbing nothing.
            On.Player.PickupCandidate += (orig, self, favorSpears) =>
            {
                if (VanillaGrab(self) || !RequestedCategory(self, out bool wantWeapon))
                {
                    return orig(self, favorSpears);
                }

                return FindBestInCategory(self, favorSpears, wantWeapon);
            };

            // 3) Vanilla also grabs without ever asking for a candidate:
            // Player.Collide picks up whatever the body just touched while a
            // pickup is pending, and that path never consults the candidate.
            // Every grab funnels through SlugcatGrab, so the category is
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
                if (obj != null
                    && !VanillaGrab(self)
                    && RequestedCategory(self, out bool wantWeapon)
                    && !MatchesCategory(obj, wantWeapon))
                {
                    return;
                }

                orig(self, obj, graspUsed);
            };
        }

        // The vanilla Grab key, read from the keybind rather than from
        // input[0].pckp: pckp is the bit we ourselves set while a category key
        // is down, so it can no longer tell the two apart. While vanilla Grab
        // is down the mod stays out of the way entirely - no narrowing and no
        // guard - so that a plain grab stays a plain grab.
        private static bool VanillaGrab(Player player)
        {
            return player.IsPressed(PlayerKeybind.Grab);
        }

        // Which category is being asked for, if any. A key held right now wins;
        // otherwise the category whose press may still be inside vanilla's
        // pickup window. The look-back reads the input history ImprovedInput
        // already keeps, so a release needs no state of our own.
        private static bool RequestedCategory(Player player, out bool wantWeapon)
        {
            if (player.IsPressed(Keybinds.GrabWeapon))
            {
                wantWeapon = true;
                return true;
            }

            if (player.IsPressed(Keybinds.GrabFood))
            {
                wantWeapon = false;
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
                    wantWeapon = true;
                    return true;
                }

                if (history[age][Keybinds.GrabFood])
                {
                    wantWeapon = false;
                    return true;
                }
            }

            wantWeapon = false;
            return false;
        }

        // Both categories are the game's own notions, so modded items follow
        // along without a list: a weapon is what the game calls a Weapon, food
        // is what the game currently calls edible.
        private static bool MatchesCategory(PhysicalObject obj, bool wantWeapon)
        {
            if (wantWeapon)
            {
                return obj is Weapon;
            }

            return obj is IPlayerEdible edible && edible.Edible;
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
        private static PhysicalObject? FindBestInCategory(Player player, float favorSpears, bool wantWeapon)
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
            // considered: it cannot invent reach. The preferred kind is kept as
            // a second best, so vanilla's own order still decides among the
            // preferred items and, when the room holds none of them, the answer
            // is exactly the one vanilla would have given.
            // Nullable on purpose: the only way this is unset is an interface
            // that never registered, and a throw from inside this hook would
            // freeze the game rather than report the problem.
            WeaponPreference prefer = wantWeapon
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
                    if (!MatchesCategory(obj, wantWeapon))
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
