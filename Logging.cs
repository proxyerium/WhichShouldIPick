using System.Text;
using BepInEx.Logging;
using RWCustom;
using UnityEngine;

namespace WhichShouldIPick
{
    // Pickup diagnostics. Debug builds only: with DEBUG undefined every member
    // below is an empty stub, so a Release dll writes nothing to the log.
    //
    // Logged events:
    //   - every PickupCandidate evaluation that lands on a grab press edge
    //   - every successful Player grab
    //   - every grab the category filter blocked
    internal static class Logging
    {
#if DEBUG
        private static ManualLogSource log = null!;

        private const string Prefix = "[WhichShouldIPick] ";

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

        // Called by Hook when the category filter refuses a grab.
        internal static void Blocked(PhysicalObject obj, int category)
        {
            log.LogInfo(Prefix + "blocked " + Describe(obj) + " category=" + category);
        }

        // Called by Hook when the mod cannot arm itself because a game method it
        // reflects on is missing. Debug builds only, like every other log line.
        internal static void ErrorDisabled(string memberName)
        {
            log.LogError(Prefix + "Player." + memberName + " not found; category filter disabled.");
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
#else
        // Release: no logging at all.
        public static void Apply(ManualLogSource source)
        {
        }

        internal static void Blocked(PhysicalObject obj, int category)
        {
        }

        internal static void ErrorDisabled(string memberName)
        {
        }
#endif
    }
}
