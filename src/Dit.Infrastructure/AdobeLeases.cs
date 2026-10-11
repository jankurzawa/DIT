using Dit.Core;

namespace Dit.Infrastructure;

public static class AdobeLeases
{
    public static void RecoverExpired(IStore store,DateTimeOffset now)
    {
        foreach(var command in store.All<AdobeCommand>("adobe").Where(c=>c.State=="inflight"&&c.RunId is not null))
        {
            var lease=store.Get<AdobeLease>("adobe-lease",command.Id);
            if(lease is null||lease.RunId!=command.RunId||lease.LastSeen<now-TimeSpan.FromSeconds(45))store.Put("adobe",command.Id,command with {State="pending",RunId=null,Error=null});
        }
    }
}
