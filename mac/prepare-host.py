"""Generate macOS adapters without editing the Windows source tree.

Exact replacement assertions deliberately fail after upstream changes instead of
silently building a Mac worker with Windows-specific discovery or disk handling.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "mac/generated"
OUT.mkdir(parents=True, exist_ok=True)

def read(name):
    return (ROOT / name).read_text(encoding="utf-8-sig")

def replace(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError(f"Windows source changed: expected one occurrence of {old[:80]!r}")
    return text.replace(old, new)

# A drive prefix/backslash is not rooted on Unix. Reject it explicitly rather
# than interpreting a Windows traversal string as an ordinary Mac filename.
policies = replace(read("src/Dit.Core/Policies.cs"), "        var canonical = Path.GetFullPath(root)", "        if(!OperatingSystem.IsWindows()&&(relative.Contains('\\\\')||Regex.IsMatch(relative,@\"^[A-Za-z]:\")))throw new SafetyException(\"Niepoprawna ścieżka względna.\");\n        var canonical = Path.GetFullPath(root)")
(OUT / "Policies.cs").write_text(policies, encoding="utf-8")

host = replace(read("src/Dit.Worker/Program.cs"), "AddSingleton<IVolumeInspector,WindowsVolumes>()", "AddSingleton<IVolumeInspector,MacVolumes>()")
host = replace(host, '"Local\\\\DIT-"', '"DIT-mac-"')
host = replace(host, "    return File.ReadAllText(path).Trim();", "    if(OperatingSystem.IsMacOS())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);\n    return File.ReadAllText(path).Trim();")
(OUT / "Program.cs").write_text(host, encoding="utf-8")

launcher = replace(read("src/Dit.Worker/AdobeLauncher.cs"), "Process.Start(new ProcessStartInfo(executable){UseShellExecute=false})", 'Process.Start(new ProcessStartInfo("/usr/bin/open"){UseShellExecute=false,ArgumentList={"-a",executable}})')
(OUT / "AdobeLauncher.cs").write_text(launcher, encoding="utf-8")

adobe = read("src/Dit.Infrastructure/AdobeSetup.cs")
start = adobe.index('    private static string? FindInstall(')
end = adobe.index('    public static string? SequencePreset(', start)
adobe = adobe[:start] + '''    private static string? FindInstall(string prefix,string executable)
    {
        foreach(var root in new[]{"/Applications",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Applications")})
            if(Directory.Exists(root))foreach(var product in Directory.GetDirectories(root,prefix+"*").OrderDescending(StringComparer.OrdinalIgnoreCase))
            {
                if(product.EndsWith(".app",StringComparison.Ordinal)&&Directory.Exists(Path.Combine(product,"Contents")))return product;
                var app=Directory.GetDirectories(product,prefix+"*.app").OrderDescending(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if(app is not null)return app;
            }
        return null;
    }
''' + adobe[end:]
adobe = replace(adobe, 'Path.Combine(Path.GetDirectoryName(executable)!,"Settings","SequencePresets","UHD (4K)","UHD (4K) 2160p 50 fps.sqpreset")', 'Path.Combine(executable,"Contents","Settings","SequencePresets","UHD (4K)","UHD (4K) 2160p 50 fps.sqpreset")')
(OUT / "AdobeSetup.cs").write_text(adobe, encoding="utf-8")

proxy = read("src/Dit.Infrastructure/ProxyPresets.cs")
start = proxy.index('    public static string AdobeWatermark()')
proxy = proxy[:start] + '''    public static string AdobeWatermark()
    {
        foreach(var app in new[]{AdobeSetup.EncoderExecutable(),AdobeSetup.PremiereExecutable()}.OfType<string>())
            foreach(var relative in new[]{"Contents/Resources/png/ProxyWatermark.png","Contents/png/ProxyWatermark.png","Contents/Resources/ProxyWatermark.png"})
            {
                var path=Path.Combine(app,relative);if(File.Exists(path))return path;
            }
        throw new SafetyException("Nie znaleziono znaku wodnego Adobe ProxyWatermark.png. Zainstaluj Media Encoder lub wybierz własny preset proxy.");
    }
}
'''
(OUT / "ProxyPresets.cs").write_text(proxy, encoding="utf-8")

# Chromium cannot decode ProRes MOV proxies. Its player uses a separate H.264/AAC
# preview; the actual Adobe proxy, watermark and automatic attach stay unchanged.
preview = replace(read("src/Dit.Infrastructure/PreviewService.cs"), "if(!thumbnail&&asset.ProxyPath", "if(!thumbnail&&!lightweight&&asset.ProxyPath")
(OUT / "PreviewService.cs").write_text(preview, encoding="utf-8")
