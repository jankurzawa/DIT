using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class SecurityTests
{
    private static (ProjectConfig C,UserAccount U,FakeClock Clock,Sessions Sessions) Setup()
    {
        using var f=new Fixture();var clock=new FakeClock();return (f.Config,new("operator","Operator",Passwords.Hash("very-long-password"),false),clock,new(clock));
    }
    [Fact] public void PasswordHashIsSaltedAndRejectsIncorrectPassword()
    {var a=Passwords.Hash("very-long-password");var b=Passwords.Hash("very-long-password");Assert.NotEqual(a,b);Assert.True(Passwords.Verify("very-long-password",a));Assert.False(Passwords.Verify("incorrect",a));Assert.False(Passwords.Verify("anything","malformed"));}
    [Theory][InlineData("1")][InlineData("short")]
    public void ShortPasswordAccepted(string value)=>Assert.True(Passwords.Verify(value,Passwords.Hash(value)));
    [Fact] public void EmptyPasswordIsSaltedAndVerified(){var hash=Passwords.Hash("");Assert.True(Passwords.Verify("",hash));Assert.False(Passwords.Verify("other",hash));Assert.NotEqual(hash,Passwords.Hash(""));}
    [Theory][InlineData(2999,true)][InlineData(3000,false)][InlineData(3001,false)]
    public void TabLocksAtThreeSeconds(int milliseconds,bool valid)
    {
        var (c,u,clock,s)=Setup();var view=s.Login(c,c.Tabs[1],u,"very-long-password");s.Leave(view.Token);clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        if(valid)s.Enter(view.Token);else Assert.Throws<AccessException>(()=>s.Enter(view.Token));
    }
    [Fact] public void ReturningEarlyCancelsTimer()
    {var (c,u,clock,s)=Setup();var view=s.Login(c,c.Tabs[1],u,"very-long-password");s.Leave(view.Token);clock.Advance(TimeSpan.FromSeconds(2));s.Enter(view.Token);clock.Advance(TimeSpan.FromSeconds(10));Assert.Equal(u.Id,s.Require(view.Token,c.Id,[u]).UserId);}
    [Theory][InlineData(299,true)][InlineData(300,false)]
    public void GlobalInactivityExpiresActiveSession(int seconds,bool valid)
    {var (c,u,clock,s)=Setup();var view=s.Login(c,c.Tabs[1],u,"very-long-password");clock.Advance(TimeSpan.FromSeconds(seconds));if(valid)s.Require(view.Token,c.Id,[u]);else Assert.Throws<AccessException>(()=>s.Require(view.Token,c.Id,[u]));}
    [Fact] public void ActivityDoesNotReviveExpiredSessions()
    {var (c,u,clock,s)=Setup();var view=s.Login(c,c.Tabs[1],u,"very-long-password");clock.Advance(TimeSpan.FromMinutes(5));s.Activity();Assert.Throws<AccessException>(()=>s.Require(view.Token,c.Id,[u]));}
    [Fact] public void UserCannotEnterOtherTabOrElevate()
    {var (c,u,_,s)=Setup();Assert.Throws<AccessException>(()=>s.Login(c,c.Tabs[0],u,"very-long-password"));var view=s.Login(c,c.Tabs[1],u,"very-long-password");Assert.Throws<AccessException>(()=>s.Require(view.Token,c.Id,[u],true));Assert.Throws<AccessException>(()=>s.Require(view.Token,"other-project",[u]));}
    [Fact] public void PasswordRevisionRevokesSessions()
    {var (c,u,_,s)=Setup();var view=s.Login(c,c.Tabs[1],u,"very-long-password");Assert.Throws<AccessException>(()=>s.Require(view.Token,c.Id,[u with {Revision=1}]));}
    [Fact] public void LockAllRevokesActiveAndInactiveSessions()
    {var (c,u,_,s)=Setup();var view=s.Login(c,c.Tabs[1],u,"very-long-password");s.LockAll();Assert.Throws<AccessException>(()=>s.Require(view.Token,c.Id,[u]));}
    [Fact] public void IdleOldWindowCannotRevokeNewLogin()
    {
        var(c,u,clock,s)=Setup();var old=s.Login(c,c.Tabs[1],u,"very-long-password");clock.Advance(TimeSpan.FromMinutes(6));
        var current=s.Login(c,c.Tabs[1],u,"very-long-password");
        for(var i=0;i<5;i++){s.Lock("");s.Lock(old.Token);clock.Advance(TimeSpan.FromSeconds(1));Assert.Equal(current.Token,s.Require(current.Token,c.Id,[u]).Token);}
    }
    [Fact] public void ScopedLockDoesNotRevokeOtherTabs()
    {
        var(c,u,_,s)=Setup();var audio=new UserAccount("audio","Audio",Passwords.Hash("1"),false);
        var camera=s.Login(c,c.Tabs[1],u,"very-long-password");var recording=s.Login(c,c.Tabs[2],audio,"1");s.Lock(camera.Token);
        Assert.Throws<AccessException>(()=>s.Require(camera.Token,c.Id,[u,audio]));Assert.Equal(recording.Token,s.Require(recording.Token,c.Id,[u,audio]).Token);
    }
}

public sealed class PolicyTests
{
    [Theory][InlineData("../escape")][InlineData("sub/../../escape")][InlineData("C:\\Windows\\system.ini")]
    public void PathTraversalBlocked(string relative){using var f=new Fixture();Assert.Throws<SafetyException>(()=>Paths.Under(f.Root,relative));}
    [Theory][InlineData("CON")][InlineData("name.")][InlineData("x/y")][InlineData("..")] public void UnsafeNamesBlocked(string name)=>Assert.Throws<ArgumentException>(()=>Paths.SafeName(name));
    [Fact] public void CollisionSuffixPreservesExtension(){Assert.Equal("C0001.MOV",Paths.CollisionName("C0001.MOV",1));Assert.Equal("C0001(2).MOV",Paths.CollisionName("C0001.MOV",2));Assert.Equal("C0001(3).MOV",Paths.CollisionName("C0001.MOV",3));}
    [Fact] public void GapsIgnoreArrivalOrderAndDuplicates(){var gaps=Numbering.Gaps(["C0004.MOV","C0001.MOV","C0002.MOV","C0002.MOV"],@"^(?<series>C)(?<number>\d+)$");Assert.Equal(new NumberGap("C",3,3),Assert.Single(gaps));}
    [Fact] public void LowerLateCardDoesNotMeanReset(){Assert.Empty(Numbering.Gaps(["C0003.MOV","C0002.MOV","C0001.MOV"],@"^(?<series>C)(?<number>\d+)$"));}
    [Fact] public void SeriesAreIndependentAndNoUnobservedPrefixGap(){Assert.Empty(Numbering.Gaps(["A0100.MOV","A0101.MOV","B0001.MOV"],@"^(?<series>[A-Z])(?<number>\d+)$"));}
    [Fact] public void NonMatchingNamesAreNotInventedNumbers(){Assert.Empty(Numbering.Gaps(["20261010_camera3_take7.MOV"],@"^(?<series>C)(?<number>\d+)$"));}
    [Fact] public void DayResolutionUsesCaptureNotIngestAndCrossesMidnight()
    {var day=new ShootingDay("day","project",1,DateTimeOffset.Parse("2026-10-10T20:00:00+02:00"),DateTimeOffset.Parse("2026-10-11T04:00:00+02:00"),true);Assert.Equal(day,Days.Resolve([day],DateTimeOffset.Parse("2026-10-11T01:00:00+02:00")));Assert.Null(Days.Resolve([day],null));Assert.Null(Days.Resolve([day],day.End));}
    [Theory][InlineData("01:00:00:24",25,36009600000L)][InlineData("00:00:00:49",50,9800000L)]
    public void TimecodeUsesSourceRate(string tc,int fps,long expected)=>Assert.Equal(expected,Timecodes.Parse(tc,fps,1));
    [Theory][InlineData("00:00:00:50")][InlineData("25:00:00:00")][InlineData("00:00:00;12")][InlineData("invalid")]
    public void InvalidTimecodeRejected(string tc)=>Assert.Null(Timecodes.Parse(tc,50,1));
    [Fact] public void AudioMappingComesFromProfileAndCameraFromTab()
    {using var f=new Fixture();Assert.Equal("cam",Configuration.ResolveDevice(f.Config,f.Config.Tabs[1],"anything.MOV"));Assert.Equal("rec",Configuration.ResolveDevice(f.Config,f.Config.Tabs[2],"REC001.wav"));Assert.Null(Configuration.ResolveDevice(f.Config,f.Config.Tabs[2],"unknown.wav"));}
    [Fact] public void SingleAudioSourceNeedsNoPattern()
    {using var f=new Fixture();var c=f.Config with {AudioSources=[f.Config.AudioSources[0] with {Pattern=""}]};Configuration.Validate(c);Assert.Equal("rec",Configuration.ResolveDevice(c,c.Tabs[2],"arbitrary.wav"));}
    [Theory][InlineData("Rejestrator 1/take.wav","rec")][InlineData("Rejestrator 1_0001.wav","rec")][InlineData("Audio2/take.wav","rec2")][InlineData("unidentified.wav",null)][InlineData("Rejestrator 1/Audio2/take.wav",null)]
    public void MultipleAudioSourcesUseNamesAndLeaveAmbiguousFilesUnassigned(string path,string? expected)
    {
        using var f=new Fixture();var c=f.Config with {Devices=[..f.Config.Devices,new("rec2","Audio2",true)],AudioSources=[f.Config.AudioSources[0] with {Pattern=""},new("rec2","",20,Enumerable.Range(1,20).Select(i=>i.ToString()).ToArray())]};
        Assert.Equal(expected,Configuration.ResolveDevice(c,c.Tabs[2],path));
    }
    [Fact] public void StorageReservesAgainstParallelJobsAndAdvancesInOrder()
    {
        using var f=new Fixture();f.Volumes.Capacity=100;var allocator=new StorageAllocator(f.Volumes);using var first=allocator.Reserve("main",[f.Main,f.Backup],70,[]);using var second=allocator.Reserve("main",[f.Main,f.Backup],50,[]);Assert.Equal(f.Main,first.Root);Assert.Equal(f.Backup,second.Root);Assert.Throws<SafetyException>(()=>allocator.Reserve("main",[f.Main,f.Backup],100,[]));
    }
    [Fact] public void SamePhysicalDiskRejected(){using var f=new Fixture();var allocator=new StorageAllocator(f.Volumes);Assert.Throws<SafetyException>(()=>allocator.Reserve("main",[f.Main],1,["main"]));}
}
