using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace valheimCLI.Extensions
{
    // First bundled module. Existing text inspection commands remain compatible.
    internal static class WorldObservations
    {
        internal static ExtensionRegistration Register(ExtensionRegistry registry) => registry.Register("valheim.world", "0.1.0", 1,
            new ExtensionCommand("terrain-grid", "Capture a bounded terrain grid: <x> <z> <spacing> <countX> <countZ> <generator|loaded-ground>", TerrainGridObservation.Read, readOnly: true, needsWorld: true),
            new ExtensionCommand("terrain-paint", "Read one loaded paint texel as raw RGBA: <integer x> <integer z>", Paint, readOnly: true, needsWorld: true),
            new ExtensionCommand("terrain-surface", "Read a loaded terrain vertex and its own collider: <x> <z> (grid vertices)", Surface, readOnly: true, needsWorld: true),
            new ExtensionCommand("player-support", "Read local player position, motion and grounded state", Support, readOnly: true, role: ExtensionRole.Client, needsWorld: true),
            new ExtensionCommand("player-support-wait", "Wait for supported arrival without remote polling: <x> <ground-y> <z> <timeout-seconds>", SupportWait, readOnly: true, role: ExtensionRole.Client, needsWorld: true),
            new ExtensionCommand("terrain", "terrain <x> <z> <generator|loaded-ground>; metres, x/z horizontal", Terrain, readOnly: true, needsWorld: true));
        private static IEnumerator Paint(ExtensionContext context)
        {
            if (context.Arguments.Count != 2 || !CommandArguments.TryFiniteFloat(context.Arguments[0], out float x) ||
                !CommandArguments.TryFiniteFloat(context.Arguments[1], out float z) || Math.Abs(x) > 20000 || Math.Abs(z) > 20000 ||
                x != Mathf.Round(x) || z != Mathf.Round(z))
            { context.Fail("usage", "terrain-paint <integer x> <integer z>; native 1m grid within +/-20000"); yield break; }
            var point = new Vector3(x, 0, z);
            var hm = Heightmap.FindHeightmap(point);
            var texture = hm != null ? hm.GetPaintMask() : null;
            int px = -1, pz = -1;
            if (hm != null) hm.WorldToVertexMask(point, out px, out pz);
            // GetPaintMask(x,z) returns black out of range. Do not report that
            // sentinel as measured unpainted ground; validate the texture first.
            bool complete = hm != null && hm.m_scale == 1 && texture != null && texture.isReadable &&
                px >= 0 && pz >= 0 && px < texture.width && pz < texture.height;
            Color paint = complete ? hm!.GetPaintMask(px, pz) : default;
            context.Succeed(new Dictionary<string, object?> {
                ["source"] = "loaded-terrain-paint", ["complete"] = complete, ["x"] = x, ["z"] = z,
                ["units"] = "rgba01", ["texelX"] = px, ["texelZ"] = pz,
                ["mapX"] = hm != null ? (object)hm.transform.position.x : null,
                ["mapZ"] = hm != null ? (object)hm.transform.position.z : null,
                ["r"] = complete ? (object)paint.r : null, ["g"] = complete ? (object)paint.g : null,
                ["b"] = complete ? (object)paint.b : null, ["a"] = complete ? (object)paint.a : null
            });
            yield break;
        }
        private static IEnumerator Surface(ExtensionContext context)
        {
            if(context.Arguments.Count!=2 || !CommandArguments.TryFiniteFloat(context.Arguments[0],out float x) ||
                !CommandArguments.TryFiniteFloat(context.Arguments[1],out float z) || Math.Abs(x)>20000 || Math.Abs(z)>20000 || x!=Mathf.Round(x) || z!=Mathf.Round(z))
            { context.Fail("usage","terrain-surface <integer x> <integer z>; native 1m grid vertices within +/-20000"); yield break; }
            var point=new Vector3(x,0,z); var hm=Heightmap.FindHeightmap(point);
            float height=0; RaycastHit hit=default;
            var collider=hm != null ? hm.GetComponent<MeshCollider>() : null;
            bool complete=hm!=null && hm.m_scale==1 && hm.GetWorldHeight(point,out height) && collider!=null &&
                collider.Raycast(new Ray(new Vector3(x,height+20,z),Vector3.down),out hit,40);
            context.Succeed(new Dictionary<string,object?>{["source"]="loaded-terrain-surface",["complete"]=complete,["x"]=x,["z"]=z,
                ["height"]=complete?(object)height:null,["colliderHeight"]=complete?(object)hit.point.y:null,["units"]="metres"});
            yield break;
        }
        private static IEnumerator Support(ExtensionContext context)
        {
            if(context.Arguments.Count!=0){context.Fail("usage","player-support takes no arguments");yield break;}
            var player=Player.m_localPlayer;
            if(player==null){context.Succeed(new Dictionary<string,object?>{["source"]="local-player-support",["complete"]=false});yield break;}
            var p=player.transform.position; var velocity=player.GetVelocity();
            context.Succeed(new Dictionary<string,object?>{["source"]="local-player-support",["complete"]=true,
                ["x"]=p.x,["y"]=p.y,["z"]=p.z,["speed"]=velocity.magnitude,["grounded"]=player.IsOnGround(),
                ["flying"]=player.IsDebugFlying(),["attached"]=player.IsAttached(),["dead"]=player.IsDead(),["teleporting"]=player.IsTeleporting(),["units"]="metres"});
            yield break;
        }
        private static IEnumerator SupportWait(ExtensionContext context)
        {
            var a = context.Arguments;
            if (a.Count != 4 || !CommandArguments.TryFiniteFloat(a[0], out float x) ||
                !CommandArguments.TryFiniteFloat(a[1], out float y) || !CommandArguments.TryFiniteFloat(a[2], out float z) ||
                !CommandArguments.TryFiniteFloat(a[3], out float timeout) || timeout <= 0f || timeout > 120f ||
                Math.Abs(x) > 20000f || Math.Abs(z) > 20000f)
            { context.Fail("usage", "player-support-wait <x> <ground-y> <z> <timeout-seconds 0..120>"); yield break; }
            Stopwatch clock = Stopwatch.StartNew();
            long heldSince = -1;
            string pending = "no local player";
            while (clock.Elapsed.TotalSeconds < timeout && !context.Cancelled)
            {
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    var p = player.transform.position;
                    float speed = player.GetVelocity().magnitude;
                    bool grounded = player.IsOnGround(), flying = player.IsDebugFlying(), attached = player.IsAttached(),
                        dead = player.IsDead(), teleporting = player.IsTeleporting();
                    if (flying || dead || attached)
                    { context.Fail("invalid_player_state", "player is flying, dead or attached; supported arrival is impossible"); yield break; }
                    if (PlayerSupportPolicy.At(p.x, p.y, p.z, x, y, z, speed, grounded, flying, attached, dead, teleporting))
                    {
                        if (heldSince < 0) heldSince = clock.ElapsedMilliseconds;
                        if (clock.ElapsedMilliseconds - heldSince >= 250)
                        {
                            context.Succeed(new Dictionary<string, object?> { ["source"] = "local-player-support", ["complete"] = true,
                                ["x"] = p.x, ["y"] = p.y, ["z"] = p.z, ["speed"] = speed, ["grounded"] = grounded,
                                ["flying"] = flying, ["attached"] = attached, ["dead"] = dead, ["teleporting"] = teleporting,
                                ["units"] = "metres", ["heldMs"] = clock.ElapsedMilliseconds - heldSince,
                                ["elapsedMs"] = clock.ElapsedMilliseconds });
                            yield break;
                        }
                        pending = "support hold";
                    }
                    else { heldSince = -1; pending = teleporting ? "teleporting" : !grounded ? "not grounded" : "not at expected supported point"; }
                }
                yield return null;
            }
            if (context.Cancelled) { context.Fail("cancelled", "support wait was cancelled without moving the player"); yield break; }
            context.Fail("support_timeout", $"{pending} after {clock.ElapsedMilliseconds} ms at ({x},{y},{z})");
        }
        private static IEnumerator Terrain(ExtensionContext context)
        {
            var args = context.Arguments;
            if (args.Count != 3 || !CommandArguments.TryFiniteFloat(args[0], out float x) || !CommandArguments.TryFiniteFloat(args[1], out float z) ||
                Math.Abs(x) > 20000 || Math.Abs(z) > 20000 || (args[2] != "generator" && args[2] != "loaded-ground"))
            { context.Fail("usage", "terrain <x> <z> <generator|loaded-ground>; within +/-20000 m"); yield break; }
            bool complete; float height = 0;
            if (args[2] == "generator")
            {
                complete = WorldGenerator.instance != null;
                if (complete) height = WorldGenerator.instance!.GetHeight(x, z);
            }
            else
            {
                Vector3 point = new Vector3(x, 0, z);
                complete = Heightmap.FindHeightmap(point) != null && ZoneSystem.instance.GetGroundHeight(point, out height);
            }
            context.Succeed(new Dictionary<string, object?>
            {
                ["source"] = args[2], ["complete"] = complete, ["x"] = x, ["z"] = z,
                ["height"] = complete ? (object)height : null, ["units"] = "metres"
            });
        }
    }
}
