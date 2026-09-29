using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImprovedInput;
using RWCustom;
using UnityEngine;

namespace WhichShouldIPick
{
    internal static class Hook
    {
        private const int CategoryNone = 0;
        private const int CategoryWeapon = 1;
        private const int CategoryFood = 2;

        // Player.CanIPickThisUp is private; bind it to our own delegate once.
        private delegate bool CanIPickThisUpDelegate(Player self, PhysicalObject obj);
        private static CanIPickThisUpDelegate? canIPickThisUp;

        // Private Player fields we need to read or keep in sync:
        //   pickUpCandidate - vanilla's reach animation and its straight-to-back
        //                     shortcut read it, so it follows the asked category.
        //   wantToPickUp    - the pickup window (set to 5 by PickupPressed);
        //                     Player.Collide grabs on collision while it is > 0.
        private static FieldInfo? fPickUpCandidate;
        private static FieldInfo? fWantToPickUp;

        // Per player: which category the last grab press asked for, and whether a
        // key is still down. The category has to outlive the key release, because
        // vanilla keeps the pickup armed for a few frames after the press.
        private sealed class PlayerState
        {
            public bool WasWeapon;
            public bool WasFood;
            public int ArmedCategory = CategoryNone;
            public int ArmedUntilFrame;
        }

        private static readonly ConditionalWeakTable<Player, PlayerState> states = new();

        public static void Apply()
        {
            MethodInfo? canPick = typeof(Player).GetMethod(
                "CanIPickThisUp",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [typeof(PhysicalObject)],
                null);

            if (canPick == null)
            {
                Logging.ErrorDisabled("CanIPickThisUp");
                return;
            }

            canIPickThisUp = (CanIPickThisUpDelegate)canPick.CreateDelegate(typeof(CanIPickThisUpDelegate));

            const BindingFlags anyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            fPickUpCandidate = typeof(Player).GetField("pickUpCandidate", anyInstance);
            fWantToPickUp = typeof(Player).GetField("wantToPickUp", anyInstance);

            // 1) Feed our keys into the game's own grab channel. Player.checkInput
            // is the one place where the game turns physical buttons into an
            // InputPackage, and it shifts input[1] = input[0] first, so OR-ing the
            // grab bit after orig gives GrabUpdate exactly the pckp signal the
            // vanilla Grab key would produce - press edge, hold, and all of it.
            // Everything downstream (pickup animation, double-tap swap, hold to
            // eat, and their timing) then runs as untouched vanilla code.
            On.Player.checkInput += (orig, self) =>
            {
                orig(self);

                if (self.input == null || self.input.Length == 0)
                {
                    return;
                }

                bool weaponKey = self.IsPressed(Keybinds.GrabWeapon);
                bool foodKey = self.IsPressed(Keybinds.GrabFood);
                PlayerState st = states.GetValue(self, _ => new PlayerState());

                if (weaponKey || foodKey)
                {
                    // InputPackage is a struct: mutate a copy, then write it back.
                    var input = self.input[0];
                    input.pckp = true;
                    self.input[0] = input;

                    // Keep vanilla's own candidate inside the asked category so its
                    // reach animation and straight-to-back shortcut aim right.
                    fPickUpCandidate?.SetValue(
                        self,
                        weaponKey ? FindPreferredWeapon(self) : FindPreferredFood(self));

                    st.ArmedCategory = weaponKey ? CategoryWeapon : CategoryFood;
                    st.ArmedUntilFrame = Time.frameCount + 40;
                }
                else if (!st.WasWeapon && !st.WasFood && !PickupArmed(self))
                {
                    st.ArmedCategory = CategoryNone;
                }

                st.WasWeapon = weaponKey;
                st.WasFood = foodKey;
            };

            // 2) Vanilla's reach path asks for a candidate; while a category is
            // asked for the answer is that category only. Nothing of it in range
            // -> null, so the reach is a no-op instead of a surprise pickup.
            On.Player.PickupCandidate += (orig, self, favorSpears) =>
            {
                int category = RequestedCategory(self);
                if (category == CategoryWeapon)
                {
                    return FindPreferredWeapon(self);
                }

                if (category == CategoryFood)
                {
                    return FindPreferredFood(self);
                }

                return orig(self, favorSpears);
            };

            // 3) Vanilla has pickup paths that never look at PickupCandidate:
            // Player.Collide grabs whatever object the body just touched while
            // wantToPickUp > 0, and that window outlives the key release. Every
            // path funnels through SlugcatGrab, so the category is enforced here
            // for the whole armed window, not just while the key is down.
            On.Player.SlugcatGrab += (orig, self, obj, graspUsed) =>
            {
                if (obj != null)
                {
                    int category = RequestedCategory(self);
                    bool wrongCategory = (category == CategoryWeapon && !(obj is Weapon))
                        || (category == CategoryFood && !(obj is IPlayerEdible));

                    if (wrongCategory)
                    {
                        Logging.Blocked(obj, category);
                        return;
                    }
                }

                orig(self, obj, graspUsed);
            };
        }

        // Which category the player is asking for right now: a key still held wins,
        // otherwise the category armed by the press while vanilla still considers a
        // pickup pending.
        private static int RequestedCategory(Player player)
        {
            PlayerState st = states.GetValue(player, _ => new PlayerState());

            if (player.IsPressed(Keybinds.GrabWeapon))
            {
                return CategoryWeapon;
            }

            if (player.IsPressed(Keybinds.GrabFood))
            {
                return CategoryFood;
            }

            if (st.ArmedCategory != CategoryNone && Time.frameCount <= st.ArmedUntilFrame && PickupArmed(player))
            {
                return st.ArmedCategory;
            }

            return CategoryNone;
        }

        private static bool PickupArmed(Player player)
        {
            return fWantToPickUp?.GetValue(player) is int wantToPickUp && wantToPickUp > 0;
        }

        // Best in-range weapon. Priority: nearest spear, then nearest other
        // weapon; null lets vanilla decide.
        private static PhysicalObject? FindPreferredWeapon(Player player)
        {
            Room? room = player.room;
            if (room == null || canIPickThisUp == null)
            {
                return null;
            }

            Vector2 pos = player.bodyChunks[0].pos;
            int playerRipple = player.abstractPhysicalObject.rippleLayer;
            bool playerBoth = player.abstractPhysicalObject.rippleBothSides;

            PhysicalObject? bestSpear = null;
            PhysicalObject? bestWeapon = null;
            float bestSpearDist = float.MaxValue;
            float bestWeaponDist = float.MaxValue;

            List<PhysicalObject>[] layers = room.physicalObjects;
            for (int i = 0; i < layers.Length; i++)
            {
                List<PhysicalObject> layer = layers[i];
                for (int j = 0; j < layer.Count; j++)
                {
                    PhysicalObject obj = layer[j];
                    if (obj is not Weapon)
                    {
                        continue;
                    }

                    AbstractPhysicalObject apo = obj.abstractPhysicalObject;
                    if (!(apo.rippleLayer == playerRipple || apo.rippleBothSides || playerBoth))
                    {
                        continue;
                    }

                    if (obj is PlayerCarryableItem carryable && carryable.forbiddenToPlayer >= 1)
                    {
                        continue;
                    }

                    // Same reach filters as vanilla Player.PickupCandidate.
                    BodyChunk chunk = obj.bodyChunks[0];
                    if (!Custom.DistLess(pos, chunk.pos, chunk.rad + 40f))
                    {
                        continue;
                    }
                    if (!Custom.DistLess(pos, chunk.pos, chunk.rad + 20f) && !room.VisualContact(pos, chunk.pos))
                    {
                        continue;
                    }

                    if (!canIPickThisUp(player, obj))
                    {
                        continue;
                    }

                    float dist = Vector2.Distance(pos, chunk.pos);
                    if (obj is Spear)
                    {
                        if (dist < bestSpearDist)
                        {
                            bestSpearDist = dist;
                            bestSpear = obj;
                        }
                    }
                    else if (dist < bestWeaponDist)
                    {
                        bestWeaponDist = dist;
                        bestWeapon = obj;
                    }
                }
            }

            return bestSpear ?? bestWeapon;
        }

        // Best in-range edible. Edibility comes from the game's own IPlayerEdible
        // (covers fruit, eggs, slime mold, batfly meat, modded food). Priority:
        // higher foodPoints, then nearer.
        private static PhysicalObject? FindPreferredFood(Player player)
        {
            Room? room = player.room;
            if (room == null || canIPickThisUp == null)
            {
                return null;
            }

            Vector2 pos = player.bodyChunks[0].pos;
            int playerRipple = player.abstractPhysicalObject.rippleLayer;
            bool playerBoth = player.abstractPhysicalObject.rippleBothSides;

            PhysicalObject? best = null;
            int bestPoints = 0;
            float bestDist = float.MaxValue;

            List<PhysicalObject>[] layers = room.physicalObjects;
            for (int i = 0; i < layers.Length; i++)
            {
                List<PhysicalObject> layer = layers[i];
                for (int j = 0; j < layer.Count; j++)
                {
                    PhysicalObject obj = layer[j];
                    if (!(obj is IPlayerEdible edible))
                    {
                        continue;
                    }

                    AbstractPhysicalObject apo = obj.abstractPhysicalObject;
                    if (!(apo.rippleLayer == playerRipple || apo.rippleBothSides || playerBoth))
                    {
                        continue;
                    }

                    if (obj is PlayerCarryableItem carryable && carryable.forbiddenToPlayer >= 1)
                    {
                        continue;
                    }

                    // Same reach filters as vanilla Player.PickupCandidate.
                    BodyChunk chunk = obj.bodyChunks[0];
                    if (!Custom.DistLess(pos, chunk.pos, chunk.rad + 40f))
                    {
                        continue;
                    }
                    if (!Custom.DistLess(pos, chunk.pos, chunk.rad + 20f) && !room.VisualContact(pos, chunk.pos))
                    {
                        continue;
                    }

                    if (!canIPickThisUp(player, obj))
                    {
                        continue;
                    }

                    int points = edible.FoodPoints;
                    if (!edible.Edible || points <= 0)
                    {
                        continue;
                    }

                    float dist = Vector2.Distance(pos, chunk.pos);
                    if (points > bestPoints || (points == bestPoints && dist < bestDist))
                    {
                        bestPoints = points;
                        bestDist = dist;
                        best = obj;
                    }
                }
            }

            return best;
        }
    }
}
