using System.Security.Cryptography;

namespace Dit.Core;

public static class Passwords
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256$600000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string encoded)
    {
        try
        {
            var p = encoded.Split('$');
            if (p.Length != 4 || p[0] != "pbkdf2-sha256" || p[1] != "600000") return false;
            var expected = Convert.FromBase64String(p[3]);
            return expected.Length == 32 && CryptographicOperations.FixedTimeEquals(expected,
                Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[2]), 600_000, HashAlgorithmName.SHA256, 32));
        }
        catch (FormatException) { return false; }
    }
}

public sealed class Sessions(IClock clock)
{
    private sealed record Entry(string ProjectId, string UserId, string TabId, bool Admin, int Revision, DateTimeOffset? LeftAt);
    private readonly Dictionary<string, Entry> entries = [];
    private readonly object gate = new();
    private DateTimeOffset lastActivity = clock.UtcNow;
    public SessionView Login(ProjectConfig config, TabProfile tab, UserAccount user, string password)
    {
        lock (gate)
        {
            if (tab.UserId != user.Id || !Passwords.Verify(password, user.PasswordHash)) throw new AccessException("Nieprawidłowe dane logowania.");
            Sweep();
            foreach (var old in entries.Where(e => e.Value.ProjectId == config.Id && e.Value.TabId == tab.Id).Select(e => e.Key).ToArray()) entries.Remove(old);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            entries[token] = new(config.Id, user.Id, tab.Id, user.Admin && tab.Kind == TabKind.Admin, user.Revision, null);
            lastActivity = clock.UtcNow;
            return new(token, user.Id, tab.Id, user.Admin && tab.Kind == TabKind.Admin);
        }
    }
    public SessionView Require(string token, string projectId, UserAccount[] users, bool admin = false)
    {
        lock (gate)
        {
            Sweep();
            if (!entries.TryGetValue(token, out var s) || s.ProjectId != projectId ||
                !users.Any(u => u.Id == s.UserId && u.Revision == s.Revision) || (admin && !s.Admin)) throw new AccessException();
            return new(token, s.UserId, s.TabId, s.Admin);
        }
    }
    public void Activity() { lock (gate) { Sweep(); lastActivity = clock.UtcNow; } }
    public void Leave(string token) { lock (gate) { Sweep(); if(entries.TryGetValue(token, out var s)) entries[token] = s with { LeftAt = clock.UtcNow }; } }
    public void Enter(string token) { lock(gate) { Sweep(); if(!entries.TryGetValue(token, out var s)) throw new AccessException(); entries[token] = s with {LeftAt = null}; } }
    public void RevokeUser(string projectId, string userId) { lock(gate) foreach(var token in entries.Where(e => e.Value.ProjectId == projectId && e.Value.UserId == userId).Select(e=>e.Key).ToArray()) entries.Remove(token); }
    public void Reconcile(ProjectConfig previous,ProjectConfig current)
    {
        lock(gate)
        {
            foreach(var pair in entries.Where(e=>e.Value.ProjectId==current.Id).ToArray())
            {
                var old=previous.Tabs.SingleOrDefault(t=>t.Id==pair.Value.TabId);var next=current.Tabs.SingleOrDefault(t=>t.Id==pair.Value.TabId);
                if(old is null||next is null||old.UserId!=next.UserId||old.Kind!=next.Kind||old.DeviceId!=next.DeviceId)entries.Remove(pair.Key);
            }
        }
    }
    public void LockAll() { lock(gate) entries.Clear(); }
    // An idle/closing window may revoke only tokens it owns. Empty legacy requests
    // must never invalidate a session created by another running window.
    public void Lock(string token) { lock(gate) entries.Remove(token); }
    private void Sweep()
    {
        if(clock.UtcNow - lastActivity >= TimeSpan.FromMinutes(5)) entries.Clear();
        foreach(var token in entries.Where(e => e.Value.LeftAt is {} t && clock.UtcNow - t >= TimeSpan.FromSeconds(3)).Select(e=>e.Key).ToArray()) entries.Remove(token);
    }
}
