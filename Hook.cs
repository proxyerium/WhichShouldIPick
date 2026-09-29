using System;
using System.Collections.Generic;
using System.Reflection;
using RWCustom;
using UnityEngine;

namespace WhichShouldIPick
{
    internal static class Hook
    {
        // Player.CanIPickThisUp is private; bind it to our own delegate once.
        private delegate bool CanIPickThisUpDelegate(Player self, PhysicalObject obj);
        private static CanIPickThisUpDelegate? canIPickThisUp;

        public static void Apply()
        {
            MethodInfo? canPick = typeof(Player).GetMethod(
                "CanIPickThisUp",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(PhysicalObject) },
                null);

            if (canPick == null)
            {
                Debug.LogError("[WhichShouldIPick] Player.CanIPickThisUp not found; weapon priority disabled.");
                return;
            }

            canIPickThisUp = (CanIPickThisUpDelegate)canPick.CreateDelegate(typeof(CanIPickThisUpDelegate));

            // Vanilla already favors spears slightly, but a closer non-weapon still
            // wins. We override the candidate so that, within pickup range, any
            // pickable weapon beats everything else, and spears beat other weapons.
            On.Player.PickupCandidate += (orig, self, favorSpears) =>
            {
                PhysicalObject? weapon = FindPreferredWeapon(self);
                return weapon ?? orig(self, favorSpears);
            };
        }

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
                    if (!(obj is Weapon))
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
    }
}
