using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace valheimCLI
{
    /// <summary>
    /// Takes the local player along with a camera move so the zones around the camera load
    /// (the game loads zones around the player, never around a free-fly camera), and keeps it out
    /// of the shot and out of harm while it is carried:
    /// <list type="bullet">
    /// <item>its renderers and lights are switched off on this machine only (the player is not in shot; others still see it),</item>
    /// <item>debug fly is on (no gravity, no fall tracking, Character.ApplyDamage and RPC_Damage ignore it),
    /// with ghost mode (creatures do not see it) and god mode as well,</item>
    /// <item>it is put at the camera's position every frame from the camera's own frame hook.</item>
    /// </list>
    /// When the move ends or stops the player goes back to where it stood (or to the ground under
    /// the camera's end) and every flag above goes back to exactly what it was.
    /// </summary>
    internal sealed class PlayerCarry
    {
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<Light> _dimmed = new List<Light>();
        private readonly bool _god;
        private readonly bool _ghost;
        private readonly bool _debugFly;
        private Vector3 _target;
        private Quaternion _targetRotation;
        private bool _returning;
        private bool _teleportAccepted;

        private PlayerCarry(Player player)
        {
            Player = player;
            Start = player.transform.position;
            StartRotation = player.transform.rotation;
            _god = player.InGodMode();
            _ghost = player.InGhostMode();
            _debugFly = player.m_debugFly;
        }

        public Player Player { get; }
        public Vector3 Start { get; }
        public Quaternion StartRotation { get; }
        public CameraCarry Landing { get; set; }
        public bool Returning => _returning;

        /// <summary>Takes the local player, or says why it cannot be carried.</summary>
        public static bool TryTake(CameraCarry landing, out PlayerCarry? carry, out string error)
        {
            carry = null;
            error = string.Empty;
            Player player = Player.m_localPlayer;
            if (player == null || player.m_nview == null || !player.m_nview.IsValid())
            {
                error = "ERROR: --carry needs a local player; use --no-carry";
                return false;
            }

            TeleportBlock block = CustomCommands.TeleportBlocker(player);
            if (block != TeleportBlock.None || player.IsTeleporting())
            {
                error = "ERROR: the player cannot be carried now (" + CustomCommands.TeleportStateFields(player) + "); use --no-carry";
                return false;
            }

            carry = new PlayerCarry(player) { Landing = landing };
            carry.Hide();
            player.SetGodMode(true);
            player.SetGhostMode(true);
            carry.SetDebugFly(true);
            return true;
        }

        private bool Alive => Player != null && Player == Player.m_localPlayer && Player.m_nview != null && Player.m_nview.IsValid();

        /// <summary>Puts the carried player at the camera; called from the camera's frame hook after the pose is set.</summary>
        public void Follow(Vector3 camera)
        {
            if (_returning || !Alive) return;
            if (!Player.m_debugFly) SetDebugFly(true);
            Place(camera, Player.transform.rotation);
        }

        /// <summary>
        /// Starts putting the player back. Answers true when it is back and every flag restored; false
        /// while a distant teleport (to a start the game has unloaded meanwhile) is still pending,
        /// which <see cref="Tick"/> finishes.
        /// </summary>
        public bool Release(Vector3 cameraEnd, out string how)
        {
            if (!Alive)
            {
                how = "player gone";
                return true;
            }

            _returning = true;
            _target = Start;
            _targetRotation = StartRotation;
            how = "start";
            if (Landing == CameraCarry.End)
            {
                if (GroundUnder(cameraEnd, out Vector3 ground))
                {
                    _target = ground;
                    _targetRotation = Quaternion.Euler(0f, Player.transform.rotation.eulerAngles.y, 0f);
                    how = "end";
                }
                else
                {
                    how = "start (no dry ground under the camera's end)";
                }
            }

            if (ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(_target))
            {
                Place(_target, _targetRotation);
                Restore();
                how += string.Format(CultureInfo.InvariantCulture, " at {0:F1},{1:F1},{2:F1}", _target.x, _target.y, _target.z);
                return true;
            }

            how += " (distant teleport: the area there had unloaded)";
            return Tick();
        }

        /// <summary>Advances a pending return; true once the player is back and restored.</summary>
        public bool Tick()
        {
            if (!_returning) return false;
            if (!Alive) return true;
            if (!_teleportAccepted)
            {
                TeleportAnswer answer = CustomCommands.RequestTeleport(Player, _target, distant: true);
                if (answer == TeleportAnswer.Accepted)
                {
                    _teleportAccepted = true;
                    return false;
                }

                if (answer == TeleportAnswer.Cooldown || answer == TeleportAnswer.InProgress) return false;
                // Dead, attached or not ours: nothing will move it; give its state back where it is.
                Restore();
                return true;
            }

            if (Player.IsTeleporting()) return false;
            Restore();
            return true;
        }

        /// <summary>At unload: put the flags back now, wherever the player is (a teleport already under way finishes by itself).</summary>
        public void Abandon()
        {
            if (!Alive) return;
            if (!_returning)
            {
                _returning = true;
                if (ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(Start)) Place(Start, StartRotation);
                else CustomCommands.RequestTeleport(Player, Start, distant: true);
            }

            Restore();
        }

        public string Describe()
        {
            string state = _returning ? (_teleportAccepted ? "returning (teleport)" : "returning") : "carried";
            return string.Format(CultureInfo.InvariantCulture, "player={0} start={1:F1},{2:F1},{3:F1} landing={4}",
                state, Start.x, Start.y, Start.z, Landing == CameraCarry.End ? "end" : "start");
        }

        private void Place(Vector3 position, Quaternion rotation)
        {
            Transform t = Player.transform;
            t.position = position;
            t.rotation = rotation;
            Rigidbody body = Player.m_body;
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            // A carried player never falls: no fall damage is owed when it lands.
            Player.m_maxAirAltitude = position.y;
        }

        private void Restore()
        {
            if (!Alive) return;
            Player.m_maxAirAltitude = Player.transform.position.y;
            SetDebugFly(_debugFly);
            Player.SetGhostMode(_ghost);
            Player.SetGodMode(_god);
            foreach (Renderer renderer in _hidden)
            {
                if (renderer != null) renderer.enabled = true;
            }

            foreach (Light light in _dimmed)
            {
                if (light != null) light.enabled = true;
            }

            _hidden.Clear();
            _dimmed.Clear();
        }

        private void Hide()
        {
            foreach (Renderer renderer in Player.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                renderer.enabled = false;
                _hidden.Add(renderer);
            }

            foreach (Light light in Player.GetComponentsInChildren<Light>(true))
            {
                if (!light.enabled) continue;
                light.enabled = false;
                _dimmed.Add(light);
            }
        }

        // As Player.ToggleDebugFly sets it, without its message: the field and the ZDO copy other peers read.
        private void SetDebugFly(bool on)
        {
            if (Player.m_debugFly == on) return;
            Player.m_debugFly = on;
            Player.m_nview.GetZDO()?.Set(ZDOVars.s_debugFly, on);
        }

        /// <summary>The first solid ground under <paramref name="point"/>, above the water, in a loaded area.</summary>
        private static bool GroundUnder(Vector3 point, out Vector3 ground)
        {
            ground = point;
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(point)) return false;
            if (!zones.GetSolidHeight(point, out float height, 0)) return false;
            if (height < zones.m_waterLevel) return false;
            ground = new Vector3(point.x, height + 0.1f, point.z);
            return true;
        }
    }

    /// <summary>
    /// Clears a shot: hostile creatures, item drops and corpses near given points. Creatures are
    /// despawned (their network object destroyed, as removedrops does to drops), not killed: a kill
    /// leaves a ragdoll that drops its loot seconds later, right in the shot. Tamed creatures,
    /// bosses, players and creatures that are pieces stay, as the game's killall leaves players and
    /// pieces. Drops are chosen as the game's removedrops chooses them (no pieces, no fish in water).
    /// </summary>
    internal static class ShotCleaner
    {
        private static readonly List<Character> Characters = new List<Character>();
        private static readonly List<ItemDrop> Drops = new List<ItemDrop>();

        public struct Counts
        {
            public int Creatures;
            public int Drops;
            public int Corpses;

            public void Add(Counts other)
            {
                Creatures += other.Creatures;
                Drops += other.Drops;
                Corpses += other.Corpses;
            }

            public override string ToString() => string.Format(CultureInfo.InvariantCulture, "creatures={0} drops={1} corpses={2}", Creatures, Drops, Corpses);
        }

        /// <summary>
        /// Removes what is within <paramref name="radius"/> of any sample. Corpses are looked up with a
        /// scene search, so a per-frame sweep leaves them out (a sweep removes creatures before they
        /// can die, and a corpse's loot is a drop the sweep removes).
        /// </summary>
        public static Counts Clean(IReadOnlyList<CameraMoveSample> near, double radius, bool all, bool corpses)
        {
            Counts counts = default;
            if (near.Count == 0 || ZNetScene.instance == null) return counts;
            Bounds bounds = BoundsOf(near, (float)radius);
            Player player = Player.m_localPlayer;
            Characters.Clear();
            Characters.AddRange(Character.GetAllCharacters());
            foreach (Character c in Characters)
            {
                if (c == null || !bounds.Contains(c.transform.position) || !Removable(c, player, all)) continue;
                if (!CameraMoveSample.NearAny(From(c.transform.position), near, radius)) continue;
                if (Despawn(c.gameObject)) counts.Creatures++;
            }

            Drops.Clear();
            Drops.AddRange(ItemDrop.s_instances);
            foreach (ItemDrop drop in Drops)
            {
                if (drop == null || !bounds.Contains(drop.transform.position) || drop.IsPiece()) continue;
                Fish fish = drop.GetComponent<Fish>();
                if (fish != null && !fish.IsOutOfWater()) continue;
                if (!CameraMoveSample.NearAny(From(drop.transform.position), near, radius)) continue;
                if (Despawn(drop.gameObject)) counts.Drops++;
            }

            if (corpses)
            {
                foreach (Ragdoll ragdoll in UnityEngine.Object.FindObjectsByType<Ragdoll>(FindObjectsSortMode.None))
                {
                    if (ragdoll == null || !bounds.Contains(ragdoll.transform.position)) continue;
                    if (!CameraMoveSample.NearAny(From(ragdoll.transform.position), near, radius)) continue;
                    // Destroying the ragdoll's object skips Ragdoll.DestroyNow, which is what spawns its loot.
                    if (Despawn(ragdoll.gameObject)) counts.Corpses++;
                }
            }

            Characters.Clear();
            Drops.Clear();
            return counts;
        }

        private static bool Removable(Character c, Player player, bool all)
        {
            if (c.IsPlayer() || c.IsTamed() || c.IsBoss() || c.GetComponent<Piece>() != null || c.IsDead()) return false;
            if (all) return true;
            return player != null ? BaseAI.IsEnemy(player, c) : c.GetFaction() != Character.Faction.AnimalsVeg;
        }

        private static bool Despawn(GameObject go)
        {
            ZNetView view = go.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            // ZNetScene.Destroy removes the object for every peer only when this peer owns it.
            view.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
            return true;
        }

        private static Bounds BoundsOf(IReadOnlyList<CameraMoveSample> samples, float radius)
        {
            Bounds bounds = new Bounds(To(samples[0].Position), Vector3.zero);
            for (int i = 1; i < samples.Count; i++) bounds.Encapsulate(To(samples[i].Position));
            bounds.Expand(radius * 2f);
            return bounds;
        }

        private static CameraVector From(Vector3 v) => new CameraVector(v.x, v.y, v.z);

        private static Vector3 To(CameraVector v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
    }

    /// <summary>
    /// Pauses the game's natural spawning for a move: SpawnSystem.m_nospawn, the flag the vanilla
    /// nospawn command sets, which SpawnSystem (biome spawns), CreatureSpawner (placed spawn points)
    /// and SpawnArea (spawner structures) all check before they spawn. It is this process's flag:
    /// it stops the spawners this game runs, which on a game it hosts is all of them, and on a client
    /// joined to a server only those in the zones it owns.
    /// </summary>
    internal static class SpawnPause
    {
        private static bool? _before;

        public static bool Active => _before.HasValue;

        public static void Hold()
        {
            if (_before.HasValue) return;
            _before = SpawnSystem.m_nospawn;
            SpawnSystem.m_nospawn = true;
        }

        public static void Release()
        {
            if (!_before.HasValue) return;
            // Someone who turned nospawn off by hand during the move keeps it off.
            if (SpawnSystem.m_nospawn) SpawnSystem.m_nospawn = _before.Value;
            _before = null;
        }
    }

    /// <summary>
    /// The game half of --check: what each sample of a move is near. Uses the game's own "solid"
    /// layers (ZoneSystem.m_solidRayMask: Default, static_solid, Default_small, piece, terrain) and
    /// only loaded colliders; a sample whose zone or zone objects are not loaded is reported as
    /// unloaded, never as clear.
    /// </summary>
    internal static class ClearanceProbe
    {
        private const int Refinements = 8;
        private static readonly Collider[] Hits = new Collider[32];

        public static List<ClearanceSample> Probe(IReadOnlyList<CameraMoveSample> samples, float clearance)
        {
            List<ClearanceSample> result = new List<ClearanceSample>(samples.Count);
            int mask = ZoneSystem.instance.m_solidRayMask;
            Dictionary<Vector2s, bool> ready = new Dictionary<Vector2s, bool>();
            bool backfaces = Physics.queriesHitBackfaces;
            try
            {
                foreach (CameraMoveSample sample in samples)
                {
                    result.Add(ProbeOne(sample, clearance, mask, ready));
                }
            }
            finally
            {
                Physics.queriesHitBackfaces = backfaces;
            }

            return result;
        }

        private static ClearanceSample ProbeOne(CameraMoveSample sample, float clearance, int mask, Dictionary<Vector2s, bool> ready)
        {
            Vector3 p = new Vector3((float)sample.Position.X, (float)sample.Position.Y, (float)sample.Position.Z);
            Vector2s zone = ZoneSystem.GetZone(p);
            if (!ready.TryGetValue(zone, out bool loaded))
            {
                loaded = ZNetScene.instance.IsAreaReady(p);
                ready[zone] = loaded;
            }

            if (!loaded) return new ClearanceSample(sample, ClearanceKind.Unloaded);

            // Inside a mesh (under the terrain, inside a rock): an overlap only meets a mesh's
            // surface, so look up. A ray that hits a face from behind before any front face started inside.
            if (Inside(p, mask, out Collider? within)) return new ClearanceSample(sample, ClearanceKind.Clip, Name(within!), 0);

            Physics.queriesHitBackfaces = false;
            int n = Physics.OverlapSphereNonAlloc(p, clearance, Hits, mask, QueryTriggerInteraction.Ignore);
            if (n == 0) return new ClearanceSample(sample, ClearanceKind.Clear);

            // The nearest collider: the smallest sphere that still meets one, to clearance / 256.
            Collider nearest = Hits[0];
            float lo = 0f;
            float hi = clearance;
            for (int i = 0; i < Refinements; i++)
            {
                float mid = (lo + hi) * 0.5f;
                int m = Physics.OverlapSphereNonAlloc(p, mid, Hits, mask, QueryTriggerInteraction.Ignore);
                if (m > 0)
                {
                    hi = mid;
                    nearest = Hits[0];
                }
                else
                {
                    lo = mid;
                }
            }

            return new ClearanceSample(sample, ClearanceKind.Clip, Name(nearest), hi);
        }

        private static bool Inside(Vector3 p, int mask, out Collider? within)
        {
            within = null;
            Physics.queriesHitBackfaces = true;
            if (!Physics.Raycast(p, Vector3.up, out RaycastHit any, 2000f, mask, QueryTriggerInteraction.Ignore)) return false;
            Physics.queriesHitBackfaces = false;
            bool front = Physics.Raycast(p, Vector3.up, out RaycastHit outside, 2000f, mask, QueryTriggerInteraction.Ignore);
            if (front && outside.distance <= any.distance + 1e-3f) return false;
            within = any.collider;
            return true;
        }

        /// <summary>The object a collider belongs to: the network object's prefab name, "terrain" for the heightmap, else the collider's object.</summary>
        private static string Name(Collider collider)
        {
            if (collider.GetComponentInParent<Heightmap>() != null) return "terrain";
            ZNetView view = collider.GetComponentInParent<ZNetView>();
            string name = view != null ? Utils.GetPrefabName(view.gameObject) : collider.gameObject.name.Replace("(Clone)", string.Empty);
            return name.Replace(' ', '_');
        }
    }
}
