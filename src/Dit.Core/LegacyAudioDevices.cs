namespace Dit.Core;

public static class LegacyAudioDevices
{
    public static ProjectConfig Upgrade(ProjectConfig config)
    {
        if(config.AudioSources is not [var legacy]||legacy.Channels is <2 or >64)return config;
        var device=config.Devices.Single(d=>d.Id==legacy.DeviceId);if(device.Name!="Audio")return config;
        var devices=config.Devices.ToList();List<AudioSource> sources=[];var number=1;
        for(var i=1;i<=legacy.Channels;i++)
        {
            while(devices.Any(d=>d.Name.Equals("Mikroport "+number,StringComparison.OrdinalIgnoreCase)))number++;
            var name="Mikroport "+number++;var id=legacy.DeviceId+"-mic-"+i;while(devices.Any(d=>d.Id==id))id+="-new";
            devices.Add(device with {Id=id,Name=name,ClearQualified=false});sources.Add(new(id,"",1,[name]));
        }
        // Keep the original device, assets and frozen ingest configurations intact.
        return config with {Devices=devices.ToArray(),AudioSources=sources.ToArray(),Revision=config.Revision+1};
    }
}
