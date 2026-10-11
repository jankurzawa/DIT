using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Dit.Desktop;

internal static class WindowTheme
{
    public static void Apply(Window window)
    {
        window.SetResourceReference(FrameworkElement.StyleProperty,"AppWindowStyle");
        if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero)return;
        Set(handle,20,1);Set(handle,35,0x0019130F);Set(handle,36,0x00F8F2EE);
    }
    private static void Set(IntPtr handle,int attribute,int value)=>DwmSetWindowAttribute(handle,attribute,ref value,sizeof(int));
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
}
