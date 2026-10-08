using valheimCLI.Extensions;
using Xunit;
namespace valheimCLI.Tests;
public class ConsolePackOwnershipTests
{
    [Fact] public void RegistrationFailureRestoresPriorCommandsAndRemovesPartialNewOnes()
    {
        var old = new object(); var table = new Dictionary<string, object>{{"old",old}};
        Assert.Throws<InvalidOperationException>(() => OwnedCommandSet<object>.Register(table, () =>
        {table["new"] = new(); table["old"] = new(); throw new InvalidOperationException();}));
        Assert.Single(table); Assert.Same(old, table["old"]);
    }
    [Fact] public void CollisionIsRefusedEvenWhenRegisterReturnsNormally()
    {
        var old = new object(); var table = new Dictionary<string, object>{{"old",old}};
        Assert.Throws<InvalidOperationException>(() => OwnedCommandSet<object>.Register(table, () => {table["new"]=new();table["old"]=new();}));
        Assert.Single(table);Assert.Same(old,table["old"]);
    }
    [Fact] public void RemovingAnotherOwnersCommandRollsBack()
    {
        var old = new object(); var table = new Dictionary<string, object>{{"old",old}};
        Assert.Throws<InvalidOperationException>(()=>OwnedCommandSet<object>.Register(table,()=>table.Remove("old")));
        Assert.Same(old,table["old"]);
    }
    [Fact] public void UnloadPreservesLaterReplacementAndForgetsOwnership()
    {
        var table=new Dictionary<string,object>();
        var owner=OwnedCommandSet<object>.Register(table,()=>{table["one"]=new();table["two"]=new();});
        var replacement=new object();table["one"]=replacement;
        Assert.False(owner.Owns("one"));Assert.True(owner.Owns("two"));
        owner.Dispose();owner.Dispose();Assert.Single(table);Assert.Same(replacement,table["one"]);Assert.False(owner.Owns("two"));
    }
    [Fact] public void LosingAnAliasDoesNotWaiveTheReplacementPermission()
    {
        CliCommandValidity.ForgetOwnCommands();var ours=new object();var theirs=new object();
        CliCommandValidity.RecordOwnCommands([ours]);CliCommandValidity.ForgetOwnCommands([ours]);
        Assert.False(CliCommandValidity.IsOwnCommand(ours));Assert.False(CliCommandValidity.IsOwnCommand(theirs));
    }
    [Fact] public void ExternalCoroutineKeepsRetiredOwnerReservedUntilSettled()
    {
        using var r=new ExtensionRegistry(new(),_=>null);var owner=r.Register("pack","1",1);
        var work=owner.TrackWork();bool retired=false,cleaned=false;
        owner.OnRetiring(()=>retired=true);owner.OnDispose(()=>cleaned=true);
        owner.Dispose();Assert.True(retired);Assert.False(cleaned);
        Assert.Throws<InvalidOperationException>(()=>r.Register("pack","2",1));
        work.Dispose();work.Dispose();Assert.True(cleaned);Assert.Empty(r.Registrations);
        r.Register("pack","2",1);
    }
    [Fact] public void RetiredOwnerCannotStartAnotherCoroutine()
    {
        using var r=new ExtensionRegistry(new(),_=>null);var owner=r.Register("pack","1",1);
        var work=owner.TrackWork();owner.Dispose();Assert.Throws<ObjectDisposedException>(()=>owner.TrackWork());work.Dispose();
    }
    [Fact] public void CleanupCannotRunInTheMiddleOfRetirementCallbacks()
    {
        using var r=new ExtensionRegistry(new(),_=>null);var owner=r.Register("pack","1",1);var work=owner.TrackWork();var calls=new List<string>();
        owner.OnRetiring(()=>{calls.Add("retire1");work.Dispose();});owner.OnRetiring(()=>calls.Add("retire2"));owner.OnDispose(()=>calls.Add("cleanup"));
        owner.Dispose();Assert.Equal(["retire1","retire2","cleanup"],calls);
    }
    [Fact] public void FailedRetirementBlocksReplacementButOtherCleanupsRun()
    {
        using var r=new ExtensionRegistry(new(),_=>null);var owner=r.Register("pack","1",1);bool removed=false;
        owner.OnRetiring(()=>throw new Exception("cannot stop"));owner.OnRetiring(()=>removed=true);
        owner.Dispose();Assert.True(removed);Assert.Contains("cannot stop",owner.CleanupError);
        Assert.Throws<InvalidOperationException>(()=>r.Register("pack","2",1));
    }
    [Fact] public void CoreShutdownClosesExternalOwnerUntilFinalLeaseReturns()
    {
        var r=new ExtensionRegistry(new(),_=>null);var owner=r.Register("pack","1",1);var work=owner.TrackWork();bool cleaned=false;owner.OnDispose(()=>cleaned=true);
        r.Dispose();Assert.True(owner.IsClosing);Assert.False(cleaned);work.Dispose();Assert.True(cleaned);Assert.Empty(r.Registrations);
    }
}
