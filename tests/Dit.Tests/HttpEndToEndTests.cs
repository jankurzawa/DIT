using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Dit.Core;
using Dit.Infrastructure;
using Xunit;

namespace Dit.Tests;

public sealed class HttpEndToEndTests
{
    [Fact][Trait("Category","E2E")]
    public async Task RealWorkerEnforcesAuthenticationSessionsOwnershipAndConfiguration()
    {
        using var f=new Fixture();var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"DIT.sln")))root=root.Parent;
        var configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var exe=Path.Combine(root!.FullName,"src","Dit.Worker","bin",configuration,"net10.0","Dit.Worker.exe");
        var data=Path.Combine(f.Root,"worker");Directory.CreateDirectory(data);
        using(var seed=new SqliteStore(Path.Combine(data,"dit.sqlite")))seed.Put("alert","test",new Alert("test","project","metadata","Brak daty nagrania",f.Clock.UtcNow,TabId:"cam",SourceRoot:f.Source,CardVolumeId:"test-card"));
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};start.ArgumentList.Add("--data");start.ArgumentList.Add(data);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        start.ArgumentList.Add("--adobe-auto-launch");start.ArgumentList.Add("false");
        start.ArgumentList.Add("--listen");start.ArgumentList.Add("http://127.0.0.1:"+port);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        try
        {
            using var http=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:"+port),Timeout=TimeSpan.FromSeconds(10)};
            for(var i=0;i<60;i++)
            {
                if(process.HasExited)throw new IOException(await stdout+await stderr);
                if(File.Exists(Path.Combine(data,"local.key")))
                {
                    http.DefaultRequestHeaders.Remove("X-DIT-Key");http.DefaultRequestHeaders.Add("X-DIT-Key",File.ReadAllText(Path.Combine(data,"local.key")).Trim());
                    try{using var health=await http.GetAsync("/health");if(health.IsSuccessStatusCode)break;}catch(HttpRequestException){}
                }
                await Task.Delay(100);
            }
            var users=new[]{new NewUser("admin","Admin","",true),new NewUser("operator","Operator","",false),new NewUser("audio","Audio","",false)};
            using var create=await http.PostAsJsonAsync("/projects",new CreateProjectRequest(f.Config,users),SqliteStore.Json);Assert.True(create.IsSuccessStatusCode,await create.Content.ReadAsStringAsync());
            using var unauthenticated=await http.GetAsync("/projects/project/assets");Assert.Equal(HttpStatusCode.Forbidden,unauthenticated.StatusCode);
            using var overview=await http.GetAsync("/projects/project/overview");overview.EnsureSuccessStatusCode();Assert.Null((await overview.Content.ReadFromJsonAsync<ProjectDayStatus>(SqliteStore.Json))!.Number);
            using var login=await http.PostAsJsonAsync("/login",new LoginRequest("project","cam","operator",""),SqliteStore.Json);login.EnsureSuccessStatusCode();var session=(await login.Content.ReadFromJsonAsync<SessionView>(SqliteStore.Json))!;
            http.DefaultRequestHeaders.Add("X-DIT-Session",session.Token);
            using var own=await http.GetAsync("/projects/project/assets");Assert.True(own.IsSuccessStatusCode);
            using var config=await http.GetAsync("/projects/project/config");Assert.Equal(HttpStatusCode.Forbidden,config.StatusCode);
            using var resolve=await http.PostAsJsonAsync("/projects/project/alerts/test/resolve",new AlertResolutionRequest(true,"Sprawdzone"),SqliteStore.Json);Assert.Equal(HttpStatusCode.Forbidden,resolve.StatusCode);
            // Reproduce the old hidden window's once-per-second tokenless locks.
            using var legacy=new HttpClient{BaseAddress=http.BaseAddress};legacy.DefaultRequestHeaders.Add("X-DIT-Key",File.ReadAllText(Path.Combine(data,"local.key")).Trim());
            for(var i=0;i<3;i++){using var idle=await legacy.PostAsync("/session/lock",null);idle.EnsureSuccessStatusCode();await Task.Delay(1000);using var stillActive=await http.GetAsync("/projects/project/assets");Assert.True(stillActive.IsSuccessStatusCode);}
            using var lockResponse=await http.PostAsync("/session/lock",null);Assert.True(lockResponse.IsSuccessStatusCode);
            using var revoked=await http.GetAsync("/projects/project/assets");Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
            http.DefaultRequestHeaders.Remove("X-DIT-Session");
            using var adminLogin=await http.PostAsJsonAsync("/login",new LoginRequest("project","admin","admin",""),SqliteStore.Json);adminLogin.EnsureSuccessStatusCode();var admin=(await adminLogin.Content.ReadFromJsonAsync<SessionView>(SqliteStore.Json))!;
            http.DefaultRequestHeaders.Add("X-DIT-Session",admin.Token);
            using var operatorLogin=await http.PostAsJsonAsync("/login",new LoginRequest("project","cam","operator",""),SqliteStore.Json);operatorLogin.EnsureSuccessStatusCode();var camera=(await operatorLogin.Content.ReadFromJsonAsync<SessionView>(SqliteStore.Json))!;
            var updated=f.Config with {Name="Film po zmianie",Devices=[..f.Config.Devices,new("mic-jan","Mikroport Jan",true)],AudioSources=[..f.Config.AudioSources,new("mic-jan","",1,["Jan"])]};
            using var configured=await http.PutAsJsonAsync("/projects/project/setup",new CreateProjectRequest(updated,[]),SqliteStore.Json);configured.EnsureSuccessStatusCode();
            using var cameraRead=new HttpRequestMessage(HttpMethod.Get,"/projects/project/assets");cameraRead.Headers.Add("X-DIT-Session",camera.Token);
            // The explicit request header replaces the default admin token on this request.
            using var cameraResponse=await http.SendAsync(cameraRead);cameraResponse.EnsureSuccessStatusCode();
            using var day=await http.PostAsJsonAsync("/projects/project/days",new {At=f.Clock.UtcNow},SqliteStore.Json);day.EnsureSuccessStatusCode();
            var opened=(await day.Content.ReadFromJsonAsync<ShootingDay>(SqliteStore.Json))!;
            using var statsResponse=await http.GetAsync("/projects/project/days/"+opened.Id+"/statistics");statsResponse.EnsureSuccessStatusCode();Assert.Equal(0,(await statsResponse.Content.ReadFromJsonAsync<DayStatistics>(SqliteStore.Json))!.Clips);
            using var workflow=await http.GetAsync("/projects/project/workflow");workflow.EnsureSuccessStatusCode();Assert.Empty((await workflow.Content.ReadFromJsonAsync<AssetWorkflow[]>(SqliteStore.Json))!);
            using var audio=await http.GetAsync("/projects/project/audio-inputs");audio.EnsureSuccessStatusCode();
            var inputs=await audio.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();Assert.Contains(inputs.EnumerateArray(),i=>i.GetProperty("deviceId").GetString()=="mic-jan"&&i.GetProperty("name").GetString()=="Mikroport Jan");
            using var adobeStatus=await http.GetAsync("/projects/project/adobe-status");adobeStatus.EnsureSuccessStatusCode();
            using var adobeClient=new HttpClient{BaseAddress=http.BaseAddress};adobeClient.DefaultRequestHeaders.Add("X-DIT-Adobe",File.ReadAllText(Path.Combine(data,"adobe.key")).Trim());
            using var deniedHandshake=await http.GetAsync("/adobe/status");Assert.Equal(HttpStatusCode.Unauthorized,deniedHandshake.StatusCode);
            using var handshake=await adobeClient.GetAsync("/adobe/status");handshake.EnsureSuccessStatusCode();
            var handshakeBody=await handshake.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();Assert.Equal(1,handshakeBody.GetProperty("pending").GetInt32());Assert.Contains("Połączono",handshakeBody.GetProperty("message").GetString());
            // A handshake reports availability but must never claim a job or touch Premiere.
            using(var snapshot=new SqliteStore(Path.Combine(data,"dit.sqlite")))Assert.Equal("pending",snapshot.Get<AdobeCommand>("adobe","setup-day:"+opened.Id)!.State);
            using var next=await adobeClient.GetAsync("/adobe/next");next.EnsureSuccessStatusCode();var creation=(await next.Content.ReadFromJsonAsync<AdobeCommand>(SqliteStore.Json))!;Assert.Empty(creation.Assets);Assert.Equal(opened.Id,creation.DayId);
            using var stale=await adobeClient.PostAsJsonAsync("/adobe/"+Uri.EscapeDataString(creation.Id)+"/heartbeat",new {},SqliteStore.Json);Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
            Assert.NotNull(creation.RunId);adobeClient.DefaultRequestHeaders.Add("X-DIT-Run",creation.RunId);
            using var heartbeat=await adobeClient.PostAsJsonAsync("/adobe/"+Uri.EscapeDataString(creation.Id)+"/heartbeat",new {},SqliteStore.Json);heartbeat.EnsureSuccessStatusCode();
            using var wait=await adobeClient.PostAsJsonAsync("/adobe/"+Uri.EscapeDataString(creation.Id)+"/result",new AdobeResult("waiting","Waiting for host setup"),SqliteStore.Json);wait.EnsureSuccessStatusCode();
            using var released=await adobeClient.PostAsJsonAsync("/adobe/"+Uri.EscapeDataString(creation.Id)+"/heartbeat",new {},SqliteStore.Json);Assert.Equal(HttpStatusCode.Conflict,released.StatusCode);
            using var invalidWorkflow=new HttpClient{BaseAddress=http.BaseAddress};invalidWorkflow.DefaultRequestHeaders.Add("X-DIT-Key",File.ReadAllText(Path.Combine(data,"local.key")).Trim());invalidWorkflow.DefaultRequestHeaders.Add("X-DIT-Session",session.Token);
            using var deniedStats=await invalidWorkflow.GetAsync("/projects/project/days/"+opened.Id+"/statistics");Assert.Equal(HttpStatusCode.Forbidden,deniedStats.StatusCode);
            using var resolved=await http.PostAsJsonAsync("/projects/project/alerts/test/resolve",new AlertResolutionRequest(true,"Sprawdzono metadane"),SqliteStore.Json);resolved.EnsureSuccessStatusCode();var alert=(await resolved.Content.ReadFromJsonAsync<Alert>(SqliteStore.Json))!;Assert.True(alert.Resolved);Assert.NotNull(alert.ResolvedAt);Assert.Equal("admin",alert.ResolvedBy);Assert.Equal("test-card",alert.CardVolumeId);
            using var reopened=await http.PostAsJsonAsync("/projects/project/alerts/test/resolve",new AlertResolutionRequest(false,"Dalsze sprawdzenie"),SqliteStore.Json);reopened.EnsureSuccessStatusCode();Assert.False((await reopened.Content.ReadFromJsonAsync<Alert>(SqliteStore.Json))!.Resolved);
            http.DefaultRequestHeaders.Remove("X-DIT-Session");using var publicDay=await http.GetAsync("/projects/project/overview");publicDay.EnsureSuccessStatusCode();var status=(await publicDay.Content.ReadFromJsonAsync<ProjectDayStatus>(SqliteStore.Json))!;Assert.Equal(1,status.Number);Assert.True(status.Open);
            http.DefaultRequestHeaders.Remove("X-DIT-Key");using var noKey=await http.GetAsync("/health");Assert.Equal(HttpStatusCode.Unauthorized,noKey.StatusCode);
        }
        finally{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();await stdout;await stderr;}
    }
}
