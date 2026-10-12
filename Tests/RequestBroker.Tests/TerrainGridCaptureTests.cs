using valheimCLI.Extensions;
using Xunit;
namespace valheimCLI.Tests;
public class TerrainGridCaptureTests
{
    private static Dictionary<string,object?> Sample(float x,float z,string layer)=>new(){["height"]=40+x-z,["complete"]=true};
    [Fact] public void CaptureYieldsAndEmitsEveryCoordinateInRowOrder()
    {
        var context=new ExtensionContext(new[]{"-4","8","2","4","8","generator"},()=>false);int calls=0;
        var run=TerrainGridCapture.Run(context,()=>"world",()=>new Dictionary<string,object?>{["worldUid"]="world"},(x,z,l)=>{calls++;return Sample(x,z,l);});
        Assert.True(run.MoveNext());Assert.Equal(16,calls);Assert.Null(context.Result);
        Assert.True(run.MoveNext());Assert.Equal(32,calls);Assert.False(run.MoveNext());
        Assert.True(context.Result!.Ok);Assert.Equal(true,context.Result.Data["complete"]);
        var rows=Assert.IsType<List<IDictionary<string,object?>>>(context.Result.Data["samples"]);
        Assert.Equal(-4f,rows[0]["x"]);Assert.Equal(8f,rows[0]["z"]);
        Assert.Equal(2f,rows[31]["x"]);Assert.Equal(22f,rows[31]["z"]);Assert.Equal(20f,rows[31]["height"]);
    }
    [Theory] [InlineData("257","1")] [InlineData("16","17")] [InlineData("0","1")] [InlineData("-1","1")]
    public void OversizedOrInvalidCaptureNeverSamples(string nx,string nz)
    {
        var c=new ExtensionContext(new[]{"0","0","1",nx,nz,"generator"},()=>false);
        var run=TerrainGridCapture.Run(c,()=>throw new Exception("identity called"),()=>throw new Exception(),Sample);
        Assert.False(run.MoveNext());Assert.Equal("usage",c.Result!.Code);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void CancellationOrWorldChangeCannotPublishPartialCapture(bool cancel)
    {
        bool cancelled=false;string id="a";
        var c=new ExtensionContext(new[]{"0","0","1","8","4","generator"},()=>cancelled);
        var run=TerrainGridCapture.Run(c,()=>id,()=>new Dictionary<string,object?>(),Sample);
        Assert.True(run.MoveNext());if(cancel)cancelled=true;else id="b";
        Assert.False(run.MoveNext());Assert.False(c.Result!.Ok);Assert.Empty(c.Result.Data);
    }
    [Fact] public void UnloadedSampleIsExplicitlyIncomplete()
    {
        var c=new ExtensionContext(new[]{"0","0","1","1","1","loaded-ground"},()=>false);
        var run=TerrainGridCapture.Run(c,()=>"a",()=>new Dictionary<string,object?>(),(x,z,l)=>new Dictionary<string,object?>{["complete"]=false,["height"]=null});
        Assert.False(run.MoveNext());Assert.True(c.Result!.Ok);Assert.Equal(false,c.Result.Data["complete"]);
    }
}
