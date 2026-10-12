using System.Collections;
using valheimCLI.Extensions;
using Xunit;
namespace valheimCLI.Tests;

public class SessionCapabilityTests
{
    private sealed class SaveHost : ISessionSave
    {
        public bool SameWorld { get; set; } = true;
        public bool IsSaving { get; set; }
        public string SkipReason { get; set; } = "";
        public uint SaveNumber { get; set; } = 8;
        public bool Started { get; set; }
        public bool Writing { get; set; }
        public int Starts;
        public void Start() { Starts++; Started = Writing = true; }
    }
    [Fact] public void SaveWaitsForPreviousWriteAndConfirmsItsOwnSaveNumber()
    {
        var host = new SaveHost { IsSaving = true }; var outcome = new SaveOutcome { TimeoutSeconds = 120 };
        SaveOutcome? result = null; Func<bool>? quiet = null;
        var run = SessionSave.Run(host, outcome, () => 0, () => false, p => quiet = p, r => result = r, (c,m) => Assert.Fail(c));
        Assert.True(run.MoveNext()); Assert.Equal(0, host.Starts);
        host.IsSaving = false; Assert.True(run.MoveNext()); Assert.Equal(1,host.Starts); Assert.False(quiet!());
        host.Writing = false; host.SaveNumber = 9; Assert.False(run.MoveNext());
        Assert.True(result!.Saved); Assert.Equal(8u,result.SaveNumberBefore); Assert.Equal(9u,result.SaveNumberAfter); Assert.True(quiet());
    }
    [Theory] [InlineData("session_flag")] [InlineData("load_error")] [InlineData("low_disk")]
    public void RefusalDoesNotIssueOrClaimASave(string reason)
    {
        var host = new SaveHost { SkipReason = reason }; SaveOutcome? result = null;
        var run = SessionSave.Run(host,new SaveOutcome(),()=>0,()=>false,_=>Assert.Fail("retained without write"),r=>result=r,(c,m)=>Assert.Fail(c));
        Assert.False(run.MoveNext());Assert.Equal(0,host.Starts);Assert.False(result!.Saved);Assert.Equal(reason,result.Skipped);
    }
    [Fact] public void AFinishedThreadWithoutSaveCounterAdvanceIsNotSuccess()
    {
        var host=new SaveHost();SaveOutcome? result=null;
        var run=SessionSave.Run(host,new SaveOutcome{TimeoutSeconds=120},()=>0,()=>false,_=>{},r=>result=r,(c,m)=>Assert.Fail(c));
        Assert.True(run.MoveNext());host.Writing=false;Assert.False(run.MoveNext());Assert.False(result!.Saved);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void WorldChangeOrCancellationBeforeSaveNeverWrites(bool change)
    {
        var host=new SaveHost{SameWorld=!change};string? error=null;
        var run=SessionSave.Run(host,new SaveOutcome(),()=>0,()=>!change,_=>{},_=>Assert.Fail("result"),(c,m)=>error=c);
        Assert.False(run.MoveNext());Assert.Equal(change?"world_changed":"cancelled",error);Assert.Equal(0,host.Starts);
    }
    [Fact] public void SaveOutlivingItsTimeoutIsFollowedAndReportsTheRealOutcome()
    {
        SaveHost host=new SaveHost();double now=0;Func<bool>? quiet=null;SaveOutcome? result=null;
        IEnumerator run=SessionSave.Run(host,new SaveOutcome{TimeoutSeconds=1},()=>now,()=>false,p=>quiet=p,r=>result=r,(c,m)=>Assert.Fail(c));
        Assert.True(run.MoveNext());now=2;Assert.True(run.MoveNext());Assert.Null(result);Assert.False(quiet!());
        host.Writing=false;host.SaveNumber=9;Assert.False(run.MoveNext());
        Assert.True(result!.Saved);Assert.True(result.Finished);Assert.True(result.PastTimeout);Assert.Equal(2000L,result.Milliseconds);
        Assert.True(quiet());Assert.Equal(1,host.Starts);
    }
    [Fact] public void SaveOutlivingItsTimeoutThatFailsReportsSaveFailedNotTimeout()
    {
        SaveHost host=new SaveHost();double now=0;SaveOutcome? result=null;
        IEnumerator run=SessionSave.Run(host,new SaveOutcome{TimeoutSeconds=1},()=>now,()=>false,_=>{},r=>result=r,(c,m)=>Assert.Fail(c));
        Assert.True(run.MoveNext());now=2;Assert.True(run.MoveNext());host.Writing=false;Assert.False(run.MoveNext());
        Assert.False(result!.Saved);Assert.True(result.PastTimeout);Assert.StartsWith("ERROR: code=save_failed ",result.Reply());
    }
    [Fact] public void EarlierSaveStillWritingAtTheTimeoutIssuesNothing()
    {
        SaveHost host=new SaveHost{IsSaving=true};double now=0;string? error=null;
        IEnumerator run=SessionSave.Run(host,new SaveOutcome{TimeoutSeconds=1},()=>now,()=>false,_=>Assert.Fail("retained without write"),_=>Assert.Fail("result"),(c,m)=>error=c);
        Assert.True(run.MoveNext());now=2;Assert.False(run.MoveNext());Assert.Equal("save_timeout",error);Assert.Equal(0,host.Starts);
    }
    [Fact] public void CancelledWhileFollowingAWriteReportsCancellationAndKeepsTheHold()
    {
        SaveHost host=new SaveHost();double now=0;bool cancelled=false;Func<bool>? quiet=null;string? error=null;
        IEnumerator run=SessionSave.Run(host,new SaveOutcome{TimeoutSeconds=1},()=>now,()=>cancelled,p=>quiet=p,_=>Assert.Fail("result"),(c,m)=>error=c);
        Assert.True(run.MoveNext());now=2;Assert.True(run.MoveNext());cancelled=true;Assert.False(run.MoveNext());
        Assert.Equal("cancelled",error);Assert.False(quiet!());host.Writing=false;Assert.True(quiet());
    }
    [Fact] public void TransitionCancellationRetainsIssuedEffectAndDoesNotRetry()
    {
        bool cancelled=false;string? completion=null;int calls=0;
        var context=new ExtensionContext(Array.Empty<string>(),()=>cancelled);
        var run=SessionTransition.Run(context,"leave",()=>calls++,()=>completion,()=>0,120);
        Assert.True(run.MoveNext());cancelled=true;Assert.False(run.MoveNext());Assert.Equal(1,calls);
        Assert.False(context.Quiescent());completion="";Assert.True(context.Quiescent());
    }
    [Fact] public void TimeoutDoesNotPretendTransitionFinishedAndCannotReleaseGateEarly()
    {
        double now=0;string? completion=null;var context=new ExtensionContext(Array.Empty<string>(),()=>false);
        var run=SessionTransition.Run(context,"join",()=>{},()=>completion,()=>now,1);
        Assert.True(run.MoveNext());now=2;Assert.False(run.MoveNext());Assert.Equal("transition_timeout",context.Result!.Code);Assert.False(context.Quiescent());
        completion="join_failed";Assert.True(context.Quiescent());completion=null;Assert.True(context.Quiescent());
    }
    [Fact] public void RetiringPackKeepsTheSharedGateUntilIssuedTransitionSettles()
    {
        var gate=new OperationGate();using var registry=new ExtensionRegistry(gate,_=>null);
        string? complete=null;int issued=0;
        var owner=registry.Register("valheim.session","test",1,new ExtensionCommand("leave","",c=>SessionTransition.Run(c,"leave",()=>issued++,()=>complete,()=>0,120)));
        registry.Begin("valheim.session/leave",Array.Empty<string>(),7,()=>false,_=>{});registry.Tick();owner.Dispose();registry.Tick();
        Assert.Equal(1,issued);Assert.False(gate.TryAcquire(8,"other"));Assert.Contains(owner,registry.Registrations);
        complete="";registry.Tick();Assert.True(gate.TryAcquire(8,"other"));Assert.DoesNotContain(owner,registry.Registrations);gate.Release(8);
    }
    [Fact] public void PreviousPasswordErrorIsNotFailureOfNewJoin()
    {
        Assert.Null(SessionReadiness.JoinResult(null,false,false,false,false,"ErrorPassword"));
        Assert.Equal("join_failed",SessionReadiness.JoinResult(null,true,false,false,false,"ErrorPassword"));
        Assert.Null(SessionReadiness.JoinResult(null,true,true,false,false,"Connected"));
        Assert.Equal("",SessionReadiness.JoinResult(null,true,true,true,false,"Connected"));
    }
    [Fact] public void ReadinessNeedsWorldAndClientPlayerButNotPlayerOnDedicatedServer()
    {
        Assert.False(SessionReadiness.WorldReady(false,true,false,false,"Connected",true));
        Assert.False(SessionReadiness.WorldReady(true,false,false,false,"Connected",false));
        Assert.False(SessionReadiness.WorldReady(true,true,true,false,"Connected",false));
        Assert.True(SessionReadiness.WorldReady(true,true,false,false,"None",false));
    }
}
