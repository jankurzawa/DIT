using System.Diagnostics;
using Dit.Core;
using Dit.Infrastructure;

public sealed class AdobeLauncher(IStore store,ILogger<AdobeLauncher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? lastAttempt=null;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pending=store.All<AdobeCommand>("adobe").FirstOrDefault(c=>c.State=="pending");
                if(store.Get<bool>("system","adobe-paired")&&pending is not null&&pending.Id!=lastAttempt&&AdobeSetup.PremiereExecutable() is {} executable)
                {
                    lastAttempt=pending.Id;
                    if(!Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)).Any())Process.Start(new ProcessStartInfo(executable){UseShellExecute=false});
                }
            }
            catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException){logger.LogWarning(ex,"Nie udało się otworzyć Premiere. Kolejka nadal oczekuje.");}
            try{await Task.Delay(5000,stoppingToken);}catch(OperationCanceledException){break;}
        }
    }
}
