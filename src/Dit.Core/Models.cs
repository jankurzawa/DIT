namespace Dit.Core;

public enum TabKind { Admin, Camera, Audio }
public enum BatchStage { Queued, Copying, BackingUp, Secured, Erasing, WaitingAssignment, WaitingApproval, WaitingAdobe, Completed, Paused, Failed, WaitingMetadata }
public sealed record Credentials(string UserId, string Password);
public sealed record LoginRequest(string ProjectId, string TabId, string UserId, string Password);
public sealed record UserAccount(string Id, string Name, string PasswordHash, bool Admin, int Revision = 0);
public sealed record TabProfile(string Id, string Name, TabKind Kind, string UserId, string? DeviceId);
public sealed record DeviceProfile(string Id, string Name, bool Audio, string NumberPattern = @"^(?<series>[A-Za-z]+)(?<number>\d+)$", long ClockOffsetTicks = 0, bool ClearQualified = false, DateTimeOffset[]? NumberingResets = null,int? LtcChannel = null,int LtcFps = 25);
public sealed record AudioSource(string DeviceId, string Pattern, int Channels, string[] ChannelNames);
public sealed record ProjectConfig(string Id, string Name, string[] MainRoots, string[] BackupRoots, string ProxyRoot, string PremiereRoot,
    TabProfile[] Tabs, DeviceProfile[] Devices, AudioSource[] AudioSources, bool CompressBackup = false, bool AutoClear = false,
    string TimeZone = "Europe/Warsaw", string? PremiereTemplate = null, string? SequencePreset = null, string? ProxyPreset = null,
    int Width = 3840, int Height = 2160, int Fps = 50, int Revision = 1, string? PremiereProjectPath = null);
public sealed record ProjectSetup(ProjectConfig Config, UserAccount[] Users);
public sealed record CreateProjectRequest(ProjectConfig Config, NewUser[] Users, Dictionary<string,string>? PasswordChanges = null);
public sealed record NewUser(string Id, string Name, string Password, bool Admin);
public sealed record ShootingDay(string Id, string ProjectId, int Number, DateTimeOffset Start, DateTimeOffset? End = null, bool Closed = false, bool HandedOff = false);
public sealed record MediaInfo(long? DurationTicks, DateTimeOffset? CapturedAt, string? Timecode, int FpsNumerator = 0, int FpsDenominator = 1, int AudioChannels = 0, bool HasVideo = false, string? CaptureMethod = null,long? LtcStartTicks = null,int Width = 0,int Height = 0,string? VideoCodec = null,string? VideoProfile = null);
public sealed record SourceEntry(string RelativePath, long Length, long ModifiedUtcTicks, string? Hash = null);
public sealed record CardSnapshot(string Root, string VolumeId, string PhysicalDiskId, SourceEntry[] Entries,SourceEntry[]? FullEntries=null);
public sealed record Replica(string Path, string VolumeId, string PhysicalDiskId, string Hash, long Bytes, bool Verified, bool Archive = false);
public sealed record Asset(string Id, string BatchId, string ProjectId, string OwnerId, string? DeviceId, string? DayId, string OriginalPath,
    string LogicalName, string Hash, long Bytes, bool IsMedia, MediaInfo? Media, Replica Main, Replica Backup,
    string? ProxyPath = null, string? PreviewPath = null, long? PositionTicks = null, string SyncMethod = "unresolved", bool Late = false);
public sealed record Batch(string Id, string ProjectId, string TabId, string ActorId, string OwnerId, ProjectConfig Config, CardSnapshot Source,
    DateTimeOffset CreatedAt, BatchStage Stage = BatchStage.Queued, string? Error = null, bool ClearStarted = false, bool ClearDone = false,
    string[]? DeletedPaths = null,string? DefaultDayId=null,string? AudioDeviceId=null);
public sealed record Alert(string Key, string ProjectId, string Kind, string Message, DateTimeOffset CreatedAt, bool Resolved = false, string? Note = null,
    string? BatchId=null,string? TabId=null,string? SourceRoot=null,string? CardVolumeId=null,DateTimeOffset? ResolvedAt=null,string? ResolvedBy=null);
public sealed record AuditEvent(string Id, DateTimeOffset At, string ProjectId, string ActorId, string Action, string Detail,
    string Level="info",string? TabId=null,string? BatchId=null,string? SourceRoot=null,string? CardVolumeId=null);
public sealed record ProjectDayStatus(int? Number,bool Open,DateTimeOffset? Start,DateTimeOffset? End,bool Paused);
public sealed record AlertResolutionRequest(bool Resolved,string Note);
public sealed record IngestRequest(string Source,string? AudioDeviceId=null,string[]? Files=null);
public sealed record AssetWorkflow(string AssetId,bool Ingested,bool BackedUp,bool Proxy,bool ProxyRequired,bool InPremiere,bool Synchronized,string SyncMethod);
public sealed record CardDayStats(string BatchId,string Tab,string Source,string VolumeId,int Clips,long Bytes,long DurationTicks,int UnknownDurations,DateTimeOffset StartedAt,DateTimeOffset? SecuredAt);
public sealed record DayStatistics(string DayId,int Clips,long Bytes,long DurationTicks,int UnknownDurations,long ElapsedTicks,CardDayStats[] Cards);
public sealed record PasswordRequest(string UserId, string? CurrentPassword, string NewPassword);
public sealed record SessionView(string Token, string UserId, string TabId, bool Admin);
public sealed record DeviceStats(string DeviceId, int Clips, long OriginalBytes, long BackupBytes, long ProxyBytes, long DurationTicks, int UnknownDurations);
public sealed record TransferProgress(string BatchId,string? File,long TotalBytes,long MainBytes,long BackupBytes,DateTimeOffset StartedAt,DateTimeOffset UpdatedAt);
public sealed record VolumeInfo(string Id, string PhysicalDiskId, long AvailableBytes, bool StableIdentity);
public sealed record AdobeCommand(string Id, string ProjectId, string DayId, string ProjectPath, string SequenceName, ProjectConfig Config,
    Asset[] Assets, TimelinePlacement[] Placements, string State = "pending", string? Error = null,string? RunId=null);
public sealed record AdobeLease(string RunId,DateTimeOffset LastSeen);
public sealed record AdobeProgress(string Stage,string? AssetId,int Completed,int Total,double Percent,DateTimeOffset UpdatedAt,string? JobId=null,string? Event=null);
public sealed record TimelinePlacement(string AssetId, string DeviceId, string TrackName, bool Audio, int TrackIndex, long StartTicks, long DurationTicks, bool Unresolved, int AudioChannels);
public sealed record AdobeResult(string State, string? Error, Dictionary<string, string>? Proxies = null);
public sealed class SafetyException(string message) : Exception(message);
public sealed class AccessException(string message = "Sesja zablokowana lub brak uprawnień.") : Exception(message);
