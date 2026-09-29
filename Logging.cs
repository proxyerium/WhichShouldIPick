using System.Text;
using BepInEx.Logging;
using RWCustom;
using UnityEngine;

namespace WhichShouldIPick
{
    // Debug logging: prints pickup details to the BepInEx console, so the
    // mod's effect on Player.PickupCandidate can be observed in-game.
    // - Every PickupCandidate evaluation logs vanilla's original pick and
    //   (in Hook.cs) what our override returns instead.
    // - Every successful Player grab logs the newly grasped object.
    internal static class Logging
    {
        private static ManualLogSource log = null!;

        public static void Apply(ManualLogSource source)
        {
            log = source;

            On.Player.PickupCandidate += (orig, self, favorSpears) =>
            {
                PhysicalObject candidate = orig(self, favorSpears);

                // PickupCandidate runs every frame (vanilla uses it for pickup
                // hints). Log only on the pickup-button press edge.
                if (self.input.Length > 1 && self.input[0].pckp && !self.input[1].pckp)
                {
                    LogCandidate(self, "pick attempt", candidate, favorSpears > 0f);
                }

                return candidate;
            };

            On.Creature.Grab += (orig, self, obj, graspUsed, graspOnOther, shareability, dominance, overrideEquallyDominant, swallow) =>
            {
                bool grabbed = orig(self, obj, graspUsed, graspOnOther, shareability, dominance, overrideEquallyDominant, swallow);
                if (grabbed && self is Player player && obj != null)
                {
                    LogGrab(player, obj);
                }

                return grabbed;
            };
        }

        private static void LogCandidate(Player player, string label, PhysicalObject? obj, bool favorSpears)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("P").Append(player.playerState.playerNumber)
              .Append(' ').Append(label)
              .Append(" favorSpears=").Append(favorSpears)
              .Append(" -> ");

            if (obj == null)
            {
                sb.Append("(null)");
            }
            else
            {
                Vector2 pos = player.bodyChunks[0].pos;
                float dist = Vector2.Distance(pos, obj.bodyChunks[0].pos);
                sb.Append(Describe(obj))
                  .Append(" dist=").Append(dist.ToString("F1"));
            }

            log.LogInfo(sb.ToString());
        }

        private static void LogGrab(Player player, PhysicalObject obj)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("P").Append(player.playerState.playerNumber)
              .Append(" GRABBED ")
              .Append(Describe(obj))
              .Append(" grasps=[");

            bool any = false;
            for (int i = 0; i < player.grasps.Length; i++)
            {
                Creature.Grasp? grasp = player.grasps[i];
                if (grasp == null)
                {
                    continue;
                }

                if (any)
                {
                    sb.Append(", ");
                }

                sb.Append(i).Append(": ").Append(Describe(grasp.grabbed));
                any = true;
            }

            if (!any)
            {
                sb.Append("(empty)");
            }

            sb.Append(']');
            log.LogInfo(sb.ToString());
        }

        private static string Describe(PhysicalObject obj)
        {
            string type = obj.GetType().Name;
            string id = obj.abstractPhysicalObject.ID.ToString();
            string extra = obj is Spear spear
                ? $" spear={(spear.spearDamageBonus >= 1f ? "normal" : "weakened")} stuck={spear.stuckInObject != null}"
                : obj is Weapon ? " weapon" : "";
            return $"{type}#{id}{extra}";
        }
    }
}
