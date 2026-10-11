using Dit.Core;

namespace Dit.Infrastructure;

public sealed class ProjectService(IStore store,Sessions sessions,IngestEngine engine)
{
    public ProjectConfig Create(CreateProjectRequest request)
    {
        var c=request.Config;if(string.IsNullOrWhiteSpace(c.ProxyPreset))c=c with {ProxyPreset=BundledPresets.Proxy720p};Configuration.Validate(c);
        if(store.Get<ProjectConfig>("project",c.Id) is not null) throw new ArgumentException("Projekt już istnieje.");
        if(request.Users.Select(u=>u.Id).Distinct().Count()!=request.Users.Length || request.Users.Count(u=>u.Admin)!=1 || c.Tabs.Any(t=>!request.Users.Any(u=>u.Id==t.UserId)) || !request.Users.Single(u=>u.Id==c.Tabs.Single(t=>t.Kind==TabKind.Admin).UserId).Admin) throw new ArgumentException("Niepoprawna konfiguracja kont.");
        var users=request.Users.Select(u=>new UserAccount(u.Id,u.Name,Passwords.Hash(u.Password),u.Admin)).ToArray();
        foreach(var root in c.MainRoots.Concat(c.BackupRoots).Append(c.ProxyRoot).Append(c.PremiereRoot)) {Directory.CreateDirectory(root);FileSafety.NoLinks(root);}
        store.Atomic(()=>{c=AdobeSetup.AssignProjectPath(store,c with {PremiereProjectPath=null});store.Put("users",c.Id,users);store.Put("project",c.Id,c);});
        engine.WriteAudit(c.Id,"admin","project-created",c.Name);return c;
    }
    public UserAccount[] Users(string project)=>store.Get<UserAccount[]>("users",project)??throw new ArgumentException("Nieznany projekt.");
    public SessionView Require(string token,string project,bool admin=false)=>sessions.Require(token,project,Users(project),admin);
    public SessionView Login(LoginRequest r)
    {
        var config=store.Get<ProjectConfig>("project",r.ProjectId)??throw new ArgumentException("Nieznany projekt.");
        var user=Users(r.ProjectId).SingleOrDefault(u=>u.Id==r.UserId)??throw new AccessException();
        var tab=config.Tabs.SingleOrDefault(t=>t.Id==r.TabId)??throw new AccessException();
        var view=sessions.Login(config,tab,user,r.Password);engine.WriteAudit(r.ProjectId,user.Id,"login",tab.Name);return view;
    }
    public void Password(string project,SessionView actor,PasswordRequest request)
    {
        if(!actor.Admin && actor.UserId!=request.UserId) throw new AccessException();
        var users=Users(project);var target=users.Single(u=>u.Id==request.UserId);
        if(!actor.Admin && !Passwords.Verify(request.CurrentPassword??"",target.PasswordHash)) throw new AccessException("Nieprawidłowe aktualne hasło.");
        var updated=target with {PasswordHash=Passwords.Hash(request.NewPassword),Revision=target.Revision+1};
        store.Put("users",project,users.Select(u=>u.Id==target.Id?updated:u).ToArray());sessions.RevokeUser(project,target.Id);
        engine.WriteAudit(project,actor.UserId,"password-changed",target.Id);
    }
    public ProjectConfig Update(ProjectConfig config,SessionView actor,bool reconcile=true)
    {
        if(!actor.Admin) throw new AccessException();Configuration.Validate(config);
        var old=store.Get<ProjectConfig>("project",config.Id)??throw new ArgumentException("Nieznany projekt.");
        if(config.Tabs.Any(t=>!Users(config.Id).Any(u=>u.Id==t.UserId))) throw new ArgumentException("Nieznane konto zakładki.");
        if(!Users(config.Id).Single(u=>u.Id==config.Tabs.Single(t=>t.Kind==TabKind.Admin).UserId).Admin)throw new ArgumentException("Zakładka Admin wymaga konta administratora.");
        config=config with {Revision=old.Revision+1,PremiereProjectPath=PremiereProjects.PathFor(old)};store.Put("project",config.Id,config);engine.WriteAudit(config.Id,actor.UserId,"configuration-changed","revision="+config.Revision);if(reconcile)sessions.Reconcile(old,config);return config;
    }
    public ProjectConfig Configure(CreateProjectRequest request,SessionView actor)
    {
        if(!actor.Admin)throw new AccessException();Configuration.Validate(request.Config);
        var users=Users(request.Config.Id).ToList();
        foreach(var user in request.Users)
        {
            if(user.Admin||users.Any(u=>u.Id==user.Id))throw new ArgumentException("Nowe konto musi mieć nowy identyfikator i rolę operatora.");
            users.Add(new(user.Id,user.Name,Passwords.Hash(user.Password),false));
        }
        if(request.Config.Tabs.Any(t=>!users.Any(u=>u.Id==t.UserId)))throw new ArgumentException("Nieznane konto zakładki.");
        if(!users.Single(u=>u.Id==request.Config.Tabs.Single(t=>t.Kind==TabKind.Admin).UserId).Admin)throw new ArgumentException("Zakładka Admin wymaga konta administratora.");
        foreach(var change in request.PasswordChanges??[])
        {
            var index=users.FindIndex(u=>u.Id==change.Key);if(index<0)throw new ArgumentException("Nieznane konto do zmiany hasła.");
            var user=users[index];users[index]=user with {PasswordHash=Passwords.Hash(change.Value),Revision=user.Revision+1};
        }
        var previous=store.Get<ProjectConfig>("project",request.Config.Id)??throw new ArgumentException("Nieznany projekt.");ProjectConfig? config=null;
        store.Atomic(()=>{
            store.Put("users",request.Config.Id,users.ToArray());config=Update(request.Config,actor,false);
            foreach(var change in request.PasswordChanges??[])engine.WriteAudit(request.Config.Id,actor.UserId,"password-changed",change.Key);
        });
        sessions.Reconcile(previous,config!);
        foreach(var change in request.PasswordChanges??[])sessions.RevokeUser(request.Config.Id,change.Key);
        return config!;
    }
    public AssetWorkflow[] Workflow(string project,SessionView actor)
    {
        var complete=store.All<AdobeCommand>("adobe").Where(c=>c.ProjectId==project&&c.State=="complete").Concat(store.All<AdobeCommand>("adobe-timeline").Where(c=>c.ProjectId==project)).ToArray();
        return Visible(project,actor).Where(a=>a.IsMedia).Select(a=>
        {
            // Only a saved Adobe cycle with matching placement is evidence of timeline synchronization.
            var placements=complete.SelectMany(c=>c.Placements).Where(p=>p.AssetId==a.Id).ToArray();
            var imported=complete.Any(c=>c.Assets.Any(x=>x.Id==a.Id&&x.Hash==a.Hash));
            var synced=placements.Any(p=>!p.Unresolved&&p.StartTicks==a.PositionTicks&&p.DurationTicks==a.Media?.DurationTicks)&&a.SyncMethod!="unresolved"&&!a.SyncMethod.Contains("approximate",StringComparison.Ordinal);
            return new AssetWorkflow(a.Id,a.Main.Verified,a.Backup.Verified,a.ProxyPath is not null&&File.Exists(a.ProxyPath),a.Media?.HasVideo==true,imported,synced,a.SyncMethod);
        }).ToArray();
    }
    public DayStatistics DayStats(string project,string dayId,IClock clock)
    {
        var day=store.Get<ShootingDay>("day",dayId)??throw new ArgumentException("Nieznany dzień.");if(day.ProjectId!=project)throw new AccessException();
        var config=store.Get<ProjectConfig>("project",project)!;
        var assets=IngestEngine.Unique(store.All<Asset>("asset").Where(a=>a.ProjectId==project&&a.DayId==dayId&&a.IsMedia).ToArray());
        var audit=store.All<AuditEvent>("audit").Where(a=>a.ProjectId==project&&a.Action=="stage").ToArray();
        var cards=assets.GroupBy(a=>a.BatchId).Select(g=>
        {
            var b=store.Get<Batch>("batch",g.Key)!;
            var secured=audit.Where(a=>a.BatchId==b.Id&&a.Detail.EndsWith(": Secured",StringComparison.Ordinal)).Select(a=>(DateTimeOffset?)a.At).FirstOrDefault();
            return new CardDayStats(b.Id,config.Tabs.FirstOrDefault(t=>t.Id==b.TabId)?.Name??b.TabId,b.Source.Root,b.Source.VolumeId,g.Count(),g.Sum(a=>a.Bytes),g.Sum(a=>a.Media?.DurationTicks??0),g.Count(a=>a.Media?.DurationTicks is null),b.CreatedAt,secured);
        }).ToArray();
        return new(dayId,assets.Length,assets.Sum(a=>a.Bytes),assets.Sum(a=>a.Media?.DurationTicks??0),assets.Count(a=>a.Media?.DurationTicks is null),Math.Max(0,((day.End??clock.UtcNow)-day.Start).Ticks),cards);
    }
    public ShootingDay OpenDay(string project,DateTimeOffset start,SessionView actor)
    {
        if(!actor.Admin) throw new AccessException();var days=store.All<ShootingDay>("day").Where(d=>d.ProjectId==project).ToArray();
        if(days.Any(d=>!d.Closed) || days.Any(d=>d.End is {} end && start<end)) throw new ArgumentException("Poprzedni dzień musi być zamknięty; zakresy dni nie mogą się pokrywać.");
        var day=new ShootingDay(Guid.NewGuid().ToString("N"),project,days.Length==0?1:days.Max(d=>d.Number)+1,start);
        store.Put("day",day.Id,day);engine.WriteAudit(project,actor.UserId,"day-opened",day.Number.ToString());
        var config=store.Get<ProjectConfig>("project",project)!;var commandId="setup-day:"+day.Id;
        store.Put("adobe",commandId,new AdobeCommand(commandId,project,day.Id,PremiereProjects.PathFor(config),PremiereProjects.SequenceFor(day),config,[],[]));return day;
    }
    public ShootingDay CloseDay(string id,DateTimeOffset end,SessionView actor)
    {
        if(!actor.Admin) throw new AccessException();var day=store.Get<ShootingDay>("day",id)??throw new ArgumentException("Nieznany dzień.");
        if(end<=day.Start||day.Closed) throw new ArgumentException("Niepoprawne zamknięcie dnia.");
        day=day with {End=end,Closed=true};store.Put("day",id,day);engine.WriteAudit(day.ProjectId,actor.UserId,"day-closed",day.Number.ToString());return day;
    }
    public Asset[] Visible(string project,SessionView actor)=>IngestEngine.Unique(store.All<Asset>("asset").Where(a=>a.ProjectId==project&&(actor.Admin||a.OwnerId==actor.UserId)).ToArray());
    public DeviceStats[] Stats(string project)
    {
        return IngestEngine.Unique(store.All<Asset>("asset").Where(a=>a.ProjectId==project&&a.IsMedia).ToArray()).GroupBy(a=>a.DeviceId??"Do przypisania")
            .Select(g=>new DeviceStats(g.Key,g.Count(),g.Sum(a=>a.Bytes),g.Sum(a=>a.Backup.Bytes),g.Sum(a=>Size(a.ProxyPath)),g.Sum(a=>a.Media?.DurationTicks??0),g.Count(a=>a.Media?.DurationTicks is null))).ToArray();
    }
    private static long Size(string? path)=>path is not null&&File.Exists(path)?new FileInfo(path).Length:0;
    public void NoteAlert(string key,string note,SessionView actor)
    {
        if(!actor.Admin) throw new AccessException();var alert=store.Get<Alert>("alert",key)??throw new ArgumentException("Nieznany alert.");
        store.Put("alert",key,alert with {Note=note});engine.WriteAudit(alert.ProjectId,actor.UserId,"alert-note",key+": "+note);
    }
    public Alert ResolveAlert(string project,string key,AlertResolutionRequest request,SessionView actor,IClock clock)
    {
        if(!actor.Admin)throw new AccessException();
        var alert=store.Get<Alert>("alert",key)??throw new ArgumentException("Nieznany alert.");if(alert.ProjectId!=project)throw new AccessException();
        if(string.IsNullOrWhiteSpace(request.Note))throw new ArgumentException("Podaj wyjaśnienie zmiany statusu alertu.");
        alert=alert with {Resolved=request.Resolved,Note=request.Note.Trim(),ResolvedAt=request.Resolved?clock.UtcNow:null,ResolvedBy=request.Resolved?actor.UserId:null};
        store.Put("alert",key,alert);engine.WriteAudit(project,actor.UserId,request.Resolved?"alert-resolved":"alert-reopened",key+": "+request.Note.Trim());return alert;
    }
}
