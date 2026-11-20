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
        /// the heightmaps the game builds. Null when no world or zone prefab is loaded, or when
        /// the zone heightmap is a distant LOD (which the builder makes another way).
        /// </summary>
        public static HeightmapBlend? Create(WorldGenerator world)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (world == null || zones == null || zones.m_zonePrefab == null)
            {
                return null;
            }
            Heightmap prefab = zones.m_zonePrefab.GetComponentInChildren<Heightmap>();
            if (prefab == null || prefab.m_width <= 0 || prefab.m_scale <= 0f || prefab.IsDistantLod)
            {
                return null;
            }
            // The zone root is placed at the zone position; the heightmap may sit on a child.
            Vector3 offset = prefab.transform.position - zones.m_zonePrefab.transform.position;
            return new HeightmapBlend(world, prefab, offset);
        }

        /// <summary>
        /// The builder's height at (x, z), in metres, before terrain modifiers. The point's own
        /// biome and GetHeight are passed in: where all four corners are that biome, the builder's
        /// height is GetBiomeHeight of it, which is GetHeight, so nothing is computed twice.
        /// </summary>
        public float At(float x, float z, Heightmap.Biome pointBiome, float pointHeight)
        {
            Locate(x, z);
            if (_biome01 == _biome00 && _biome10 == _biome00 && _biome11 == _biome00)
            {
                return _biome00 == pointBiome ? pointHeight : _world.GetBiomeHeight(_biome00, x, z, out Color _);
            }
            float tx = DUtils.SmoothStep(0f, 1f, Fraction(x, _corner.x));
            float tz = DUtils.SmoothStep(0f, 1f, Fraction(z, _corner.z));
            float height00 = _world.GetBiomeHeight(_biome00, x, z, out Color _);
            float height10 = _world.GetBiomeHeight(_biome10, x, z, out Color _);
            float height01 = _world.GetBiomeHeight(_biome01, x, z, out Color _);
            float height11 = _world.GetBiomeHeight(_biome11, x, z, out Color _);
            float near = DUtils.Lerp(height00, height10, tx);
            float far = DUtils.Lerp(height01, height11, tx);
            return DUtils.Lerp(near, far, tz);
        }

        /// <summary>
        /// HeightmapBuilder.Build weights vertex l by l / width. A point on a vertex gets that
        /// vertex's own index, whatever the scale's rounding; any other point its fractional place.
        /// </summary>
        private float Fraction(float coordinate, float corner)
        {
            double vertex = ((double)coordinate - corner) / _scale;
            double nearest = Math.Round(vertex);
            if (Math.Abs(vertex - nearest) < 1e-4)
            {
                vertex = nearest;
            }
            return (float)(vertex / _width);
        }

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
            _biome00 = CornerBiome(corner.x, corner.z);
            _biome10 = CornerBiome(farX, corner.z);
            _biome01 = CornerBiome(corner.x, farZ);
            _biome11 = CornerBiome(farX, farZ);
            _corner = corner;
            _zone = zone;
            _hasZone = true;
        }

        private Heightmap.Biome CornerBiome(float x, float z)
        {
            long key = ((long)BitConverter.ToInt32(BitConverter.GetBytes(x), 0) << 32) | (uint)BitConverter.ToInt32(BitConverter.GetBytes(z), 0);
            if (!_cornerBiomes.TryGetValue(key, out Heightmap.Biome biome))
            {
                biome = _world.GetBiome(x, z);
                _cornerBiomes[key] = biome;
            }
            return biome;
        }
    }
}
