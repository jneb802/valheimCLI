using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using valheimCLI.Extensions;
namespace valheimCLI;

internal static class TerrainGridObservation
{
    internal static IEnumerator Read(ExtensionContext context)
    {
        var world=ZNet.World;var generator=WorldGenerator.instance;var network=ZNet.instance;
        string Identity()=>world!=null && ZNet.World==world && ZNet.instance==network && WorldGenerator.instance==generator
            ? world.m_uid.ToString(CultureInfo.InvariantCulture) : "";
        IDictionary<string,object?> Metadata()=>new Dictionary<string,object?> {
            ["worldUid"]=world!.m_uid.ToString(CultureInfo.InvariantCulture),["worldGenVersion"]=world.m_worldGenVersion,
            ["gameVersion"]=global::Version.GetVersionString(),
            ["gameAssemblyId"]=typeof(WorldGenerator).Assembly.ManifestModule.ModuleVersionId.ToString() };
        IDictionary<string,object?> Sample(float x,float z,string layer)
        {
            float height=0,weight=0,width=0;string? biome=null;bool complete;
            if(layer=="generator")
            {
                complete=generator!=null;
                if(complete){height=generator!.GetHeight(x,z);biome=generator.GetBiome(x,z).ToString();generator.GetRiverWeight(x,z,out weight,out width);}
            }
            else
            {
                var point=new Vector3(x,0,z);var map=Heightmap.FindHeightmap(point);
                complete=map!=null && map.GetWorldHeight(point,out height);
            }
            complete=complete && Finite(height) && Finite(weight) && Finite(width);
            return new Dictionary<string,object?> { ["complete"]=complete,["height"]=complete?(object)height:null,
                ["biome"]=complete?biome:null,["riverWeight"]=complete && layer=="generator"?(object)weight:null,
                ["riverWidth"]=complete && layer=="generator"?(object)width:null };
        }
        return TerrainGridCapture.Run(context,Identity,Metadata,Sample);
    }
    private static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
}
