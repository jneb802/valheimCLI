using System;
using HarmonyLib;

namespace valheimCLI
{
    /// <summary>
    /// Makes the game's ghost mode hold on every peer that loads this pack. The game keeps ghost mode in a field of the
    /// player's own process and never replicates it, while a creature's AI runs in whichever process owns the creature.
    /// So a creature another client simulated could see, hear and hunt a player in ghost mode (cli_set_player_safety or
    /// the ghost cheat). The player's own process now writes its ghost mode to the player's ZDO whenever it is set, and
    /// every other process reads it from there (<see cref="PlayerModes.GhostSeenBy"/>). The game's other ghost-mode checks
    /// on other processes follow too, as they would in the player's own: a creature hit by a player in ghost mode is marked
    /// cheated, and an egg does not hatch next to one. God mode, damage and the game's own ghost command are unchanged. A
    /// peer without this pack still reads the local field.
    /// </summary>
    public static class GhostReplication
    {
        public const string HarmonyId = "valheimCLI.standard.ghost";
        /// <summary>The ZDO key: 1 in ghost mode, 0 not; absent until the player's own process first sets ghost mode.</summary>
        public static readonly int Key = "valheimCLI.ghost".GetStableHashCode();
        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        private static Harmony? _harmony;

        public static void Patch()
        {
            if (_harmony != null) return;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.SetGhostMode)), postfix: new HarmonyMethod(typeof(GhostReplication), nameof(AfterSetGhostMode)));
            _harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.InGhostMode)), postfix: new HarmonyMethod(typeof(GhostReplication), nameof(AfterInGhostMode)));
            // A live reload: the ZDO may hold what an earlier load wrote, so the current mode is written now.
            if (Player.m_localPlayer is Player local) Write(local, local.InGhostMode());
        }

        /// <summary>
        /// Writes 0 before unpatching: nothing would correct the ZDO once the patches are gone, and other peers then read
        /// the player as the game itself does (never a ghost).
        /// </summary>
        public static void Unpatch()
        {
            if (_harmony == null) return;
            // At quit the player and its ZDO may already be torn down; the unpatch must still happen.
            try { if (Player.m_localPlayer is Player local && local) Write(local, false); }
            catch (Exception) { }
            _harmony.UnpatchSelf();
            _harmony = null;
        }

        /// <summary>The ghost mode on the player's ZDO, or null when it carries none or has no ZDO.</summary>
        public static bool? Replicated(Player player) => View(player) is ZNetView view ? Read(view.GetZDO()) : null;

        private static void AfterSetGhostMode(Player __instance, bool ghostmode) => Write(__instance, ghostmode);

        private static void AfterInGhostMode(Player __instance, ref bool __result)
        {
            if (View(__instance) is ZNetView view && !view.IsOwner()) __result = PlayerModes.GhostSeenBy(__result, false, Read(view.GetZDO()));
        }

        // Only the player's own process writes: the owner's write is what replicates.
        private static void Write(Player player, bool ghost)
        {
            if (View(player) is ZNetView view && view.IsOwner()) view.GetZDO().Set(Key, ghost ? 1 : 0);
        }

        private static bool? Read(ZDO zdo) => PlayerModes.ReplicatedGhost(zdo.GetInt(Key, -1));

        private static ZNetView? View(Player player) => NView(player) is ZNetView view && view.IsValid() ? view : null;
    }
}
