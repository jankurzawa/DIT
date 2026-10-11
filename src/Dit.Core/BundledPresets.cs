namespace Dit.Core;

public static class BundledPresets
{
    // Store a stable identifier; resolve against the running package after upgrades.
    public const string Proxy720p="dit:proxy-720p";
    public const string Proxy720pLabel="DIT Proxy 720p (domyślny)";
    public static bool IsDefault(string? value)=>value==Proxy720p;
    public static bool IsDefaultOrResolved(string? value)=>IsDefault(value)||(!string.IsNullOrWhiteSpace(value)&&string.Equals(Path.GetFullPath(value),Path.Combine(AppContext.BaseDirectory,"Presets","DIT Proxy 720p.epr"),StringComparison.OrdinalIgnoreCase));
    public static string? ResolveProxy(string? value)
    {
        if(!IsDefault(value))return value;
        var path=Path.Combine(AppContext.BaseDirectory,"Presets","DIT Proxy 720p.epr");
        if(!File.Exists(path))throw new SafetyException("Brakuje domyślnego presetu DIT Proxy 720p w plikach programu. Zainstaluj ponownie DIT lub wybierz własny preset.");
        return path;
    }
}
