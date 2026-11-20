using System;
using System.Collections.Generic;
using UnityEngine;

namespace valheimCLI
{
    /// <summary>
    /// The height the game's HeightmapBuilder builds for a zone heightmap before terrain
    /// modifiers. WorldGenerator.GetHeight uses only the point's own biome; the builder instead
    /// takes the biomes at the heightmap's four corners and, unless all four are one biome,
    /// mixes their GetBiomeHeight at the point with smoothstepped weights of the point's place in
    /// the heightmap. This repeats HeightmapBuilder.Build's arithmetic for a zone (not
    /// distant-LOD) heightmap, using the game's own DUtils for the weights and the mix, so at a
    /// heightmap vertex (every whole metre) it is the value the builder stores there. Between
    /// vertices it is the same formula at the point, not the game's interpolation between
    /// vertices.
    ///
    /// The heightmap is the zone's (ZoneSystem.GetZone). A point on a zone edge belongs to two
    /// heightmaps, and both give the same value there: DUtils.Lerp returns an end exactly, the
    /// edge's two corners are the same points for both, and its mix of two equal heights is
    /// that height (computed in double and rounded back to float).
    ///
    /// Not thread-safe; one instance per command. Corner biomes are remembered, since four
    /// zones share each corner.
    /// </summary>
    internal sealed class HeightmapBlend
    {
        private readonly WorldGenerator _world;
        private readonly int _width;
        private readonly float _scale;
        private readonly Vector3 _offset;
        private readonly Dictionary<long, Heightmap.Biome> _cornerBiomes = new Dictionary<long, Heightmap.Biome>();
        private bool _hasZone;
        private Vector2s _zone;
        private Vector3 _corner;
        private Heightmap.Biome _biome00;
        private Heightmap.Biome _biome10;
        private Heightmap.Biome _biome01;
        private Heightmap.Biome _biome11;

        private HeightmapBlend(WorldGenerator world, Heightmap prefab, Vector3 offset)
        {
            _world = world;
            _width = prefab.m_width;
            _scale = prefab.m_scale;
            _offset = offset;
        }

        /// <summary>
        /// Reads the zone heightmap's size from the game's own zone prefab, so the blend follows
        /// the heightmaps the game builds. Null, with the reason, when there is no world, no zone
        /// system or zone prefab yet, a zone heightmap that is not one zone in size (the blend
        /// picks the heightmap by zone), or a distant-LOD zone heightmap (built another way).
        /// </summary>
        public static HeightmapBlend? Create(WorldGenerator world, out string reason)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (world == null)
            {
                reason = "no world generator";
                return null;
            }
            if (zones == null || zones.m_zonePrefab == null)
            {
                reason = "no zone system or zone prefab loaded yet";
                return null;
            }
            Heightmap prefab = zones.m_zonePrefab.GetComponentInChildren<Heightmap>();
            if (prefab == null)
            {
                reason = "the zone prefab has no heightmap";
                return null;
            }
            if (prefab.IsDistantLod)
            {
                reason = "the zone prefab's heightmap is a distant LOD";
                return null;
            }
            if (prefab.m_width <= 0 || (double)prefab.m_width * prefab.m_scale != ZoneSystem.c_ZoneSizeDouble)
            {
                reason = FormattableString.Invariant($"the zone prefab's heightmap is {prefab.m_width} x {prefab.m_scale} m, not one {ZoneSystem.c_ZoneSize} m zone");
                return null;
            }
            // The zone root is placed at the zone position; the heightmap may sit on a child.
            Vector3 offset = prefab.transform.position - zones.m_zonePrefab.transform.position;
            reason = "";
            return new HeightmapBlend(world, prefab, offset);
        }

        /// <summary>
        /// The builder's height at (x, z), in metres, before terrain modifiers. The point's own
        /// biome and GetHeight are passed in: GetHeight is GetBiomeHeight of the point's biome, so
        /// a corner of that biome reuses it, and each other distinct corner biome is asked once.
        /// </summary>
        public float At(float x, float z, Heightmap.Biome pointBiome, float pointHeight)
        {
            Locate(x, z);
            float height00 = BiomeHeight(_biome00, x, z, pointBiome, pointHeight);
            if (_biome01 == _biome00 && _biome10 == _biome00 && _biome11 == _biome00)
            {
                return height00;
            }
            float height10 = _biome10 == _biome00 ? height00 : BiomeHeight(_biome10, x, z, pointBiome, pointHeight);
            float height01 = _biome01 == _biome00 ? height00
                : _biome01 == _biome10 ? height10 : BiomeHeight(_biome01, x, z, pointBiome, pointHeight);
            float height11 = _biome11 == _biome00 ? height00
                : _biome11 == _biome10 ? height10
                : _biome11 == _biome01 ? height01 : BiomeHeight(_biome11, x, z, pointBiome, pointHeight);
            // HeightmapBuilder.Build weights vertex l by SmoothStep(0, 1, l / width).
            float tx = DUtils.SmoothStep(0f, 1f, Fraction(x, _corner.x));
            float tz = DUtils.SmoothStep(0f, 1f, Fraction(z, _corner.z));
            float near = DUtils.Lerp(height00, height10, tx);
            float far = DUtils.Lerp(height01, height11, tx);
            return DUtils.Lerp(near, far, tz);
        }

        private float BiomeHeight(Heightmap.Biome biome, float x, float z, Heightmap.Biome pointBiome, float pointHeight) =>
            biome == pointBiome ? pointHeight : _world.GetBiomeHeight(biome, x, z, out Color _);

        /// <summary>The point's place in its heightmap, in heightmap widths (Build's l / width at a vertex).</summary>
        private float Fraction(float coordinate, float corner) =>
            (float)(((double)coordinate - corner) / _scale / _width);

        private void Locate(float x, float z)
        {
            Vector2s zone = ZoneSystem.GetZone(new Vector3(x, 0f, z));
            if (_hasZone && zone == _zone)
            {
                return;
            }
            // HeightmapBuilder.Build: the corner is the centre less half the heightmap, in float;
            // the far corners add the full size in double.
            Vector3 center = ZoneSystem.GetZonePos(zone) + _offset;
            Vector3 corner = center + new Vector3((float)_width * _scale * -0.5f, 0f, (float)_width * _scale * -0.5f);
            float farX = (float)((double)corner.x + (double)_width * (double)_scale);
            float farZ = (float)((double)corner.z + (double)_width * (double)_scale);
            // Create checked the heightmap is one zone, so a zone's far corner is its neighbour's
            // near corner: corners are keyed by zone index.
            _biome00 = CornerBiome(zone.x, zone.y, corner.x, corner.z);
            _biome10 = CornerBiome(zone.x + 1, zone.y, farX, corner.z);
            _biome01 = CornerBiome(zone.x, zone.y + 1, corner.x, farZ);
            _biome11 = CornerBiome(zone.x + 1, zone.y + 1, farX, farZ);
            _corner = corner;
            _zone = zone;
            _hasZone = true;
        }

        private Heightmap.Biome CornerBiome(int cornerX, int cornerZ, float x, float z)
        {
            long key = ((long)cornerX << 32) | (uint)cornerZ;
            if (!_cornerBiomes.TryGetValue(key, out Heightmap.Biome biome))
            {
                biome = _world.GetBiome(x, z);
                _cornerBiomes[key] = biome;
            }
            return biome;
        }
    }
}
