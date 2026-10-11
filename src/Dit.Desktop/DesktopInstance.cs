using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace Dit.Desktop;

internal sealed class DesktopInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activate;
    private readonly bool owns;
    private RegisteredWaitHandle? listener;
    public bool IsPrimary=>owns;
    public DesktopInstance(string? scope=null)
    {
        var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DIT");
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((scope??data).ToUpperInvariant())));
        mutex=new Mutex(true,"Local\\DIT-Desktop-"+key,out owns);
        activate=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DIT-Activate-"+key);
    }
    public bool Attach(Window window)
    {
        if(!owns){activate.Set();return false;}
        listener=ThreadPool.RegisterWaitForSingleObject(activate,(_,_)=>window.Dispatcher.BeginInvoke(new Action(()=>
        {
            window.Show();if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.Activate();
        })),null,Timeout.Infinite,false);
        return true;
    }
    public void Dispose(){listener?.Unregister(null);activate.Dispose();if(owns)mutex.ReleaseMutex();mutex.Dispose();}
}
