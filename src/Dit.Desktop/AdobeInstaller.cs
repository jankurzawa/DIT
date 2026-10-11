using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dit.Desktop;

public static class AdobeInstaller
{
    public static bool Installed(string version)=>File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Adobe","UXP","Plugins","External","pl.dit.media.workspace_"+version,"manifest.json"));
    public static async Task EnsureInstalled()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"adobe");var manifest=Path.Combine(root,"manifest.json");
        if(!File.Exists(manifest))throw new IOException("Brak panelu Adobe w pakiecie DIT.");
        using var json=JsonDocument.Parse(await File.ReadAllTextAsync(manifest));var version=json.RootElement.GetProperty("version").GetString()!;
        if(Installed(version))return;
        var installer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),"Adobe","Adobe Desktop Common","RemoteComponents","UPI","UnifiedPluginInstallerAgent","UnifiedPluginInstallerAgent.exe");
        var package=Path.Combine(root,"DIT-Premiere.ccx");
        if(!File.Exists(installer)||!File.Exists(package))throw new IOException("Nie znaleziono instalatora Adobe lub pakietu panelu. Zainstaluj panel z DIT-Premiere.ccx przez Creative Cloud.");
        var start=new ProcessStartInfo(installer){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("/install");start.ArgumentList.Add(package);
        using var process=Process.Start(start)??throw new IOException("Nie udało się uruchomić instalatora panelu Adobe.");
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));}catch(TimeoutException){throw new IOException("Instalacja panelu nadal trwa. Sprawdź komunikat Creative Cloud i spróbuj ponownie po zakończeniu.");}
        await Task.WhenAll(output,error);
        if(process.ExitCode!=0||!Installed(version))throw new IOException("Adobe nie potwierdziło instalacji panelu DIT (kod "+process.ExitCode+"). Sprawdź Creative Cloud → Wtyczki.");
    }
}
