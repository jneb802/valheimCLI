using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using valheimCLI.Extensions;

namespace valheimCLI;

// Engine-free capture loop; Unity supplies only sampling and identity callbacks.
internal static class TerrainGridCapture
{
    internal const int MaximumSamples = 256;
    internal const int SamplesPerFrame = 16;
    internal static IEnumerator Run(ExtensionContext context, Func<string> identity,
        Func<IDictionary<string,object?>> metadata, Func<float,float,string,IDictionary<string,object?>> sample)
    {
        var a=context.Arguments;
        if(a.Count!=6 || !CommandArguments.TryFiniteFloat(a[0],out float x) || !CommandArguments.TryFiniteFloat(a[1],out float z) ||
            !CommandArguments.TryFiniteFloat(a[2],out float step) || step<.25f || step>256 ||
            !int.TryParse(a[3],NumberStyles.None,CultureInfo.InvariantCulture,out int nx) ||
            !int.TryParse(a[4],NumberStyles.None,CultureInfo.InvariantCulture,out int nz) ||
            nx<1 || nz<1 || nx>MaximumSamples || nz>MaximumSamples || (long)nx*nz>MaximumSamples ||
            Math.Abs(x)>20000 || Math.Abs(z)>20000 || Math.Abs(x+(nx-1)*step)>20000 || Math.Abs(z+(nz-1)*step)>20000 ||
            (a[5]!="generator" && a[5]!="loaded-ground"))
        {context.Fail("usage","terrain-grid <x> <z> <spacing .25..256> <countX> <countZ> <generator|loaded-ground>; 1..256 samples, +/-20000m");yield break;}
        string id=identity();
        if(string.IsNullOrEmpty(id)){context.Fail("no_world","No world identity available.");yield break;}
        var result=new Dictionary<string,object?>(metadata());
        var rows=new List<IDictionary<string,object?>>(nx*nz); bool complete=true;
        string started=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        for(int i=0;i<nx*nz;i++)
        {
            if(context.Cancelled){context.Fail("cancelled","Capture cancelled; no complete grid published.");yield break;}
            if(identity()!=id){context.Fail("world_changed","World changed during capture.");yield break;}
            float wx=x+(i%nx)*step,wz=z+(i/nx)*step;
            var row=new Dictionary<string,object?>(sample(wx,wz,a[5]));row["x"]=wx;row["z"]=wz;
            if(!row.TryGetValue("complete",out object? ok) || !(ok is bool flag) || !flag)complete=false;
            rows.Add(row);
            if((i+1)%SamplesPerFrame==0)yield return null;
        }
        if(context.Cancelled || identity()!=id){context.Fail("capture_interrupted","Capture cancelled or world changed.");yield break;}
        result["source"]="terrain-grid";result["formatVersion"]=1;result["complete"]=complete;
        result["layer"]=a[5];result["units"]="metres";result["originX"]=x;result["originZ"]=z;
        result["spacing"]=step;result["countX"]=nx;result["countZ"]=nz;result["samples"]=rows;
        result["startedUtc"]=started;result["finishedUtc"]=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        result["consistency"]="per-sample";
        context.Succeed(result);
    }
}
