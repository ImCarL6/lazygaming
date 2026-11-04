using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LazyGaming;

public sealed class Security
{
    public string Identity { get; }
    public string Code { get; private set; } = "";
    byte[]? key;
    public bool Paired { get { lock (gate) return key != null; } }
    readonly string file;
    DateTime expires;
    int attempts;
    readonly Dictionary<string, long> nonces = [];
    readonly object gate = new();
    public Security(string directory)
    {
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "pairing.json");
        if (File.Exists(file))
        {
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(file))!;
            Identity = stored.Identity; key = Convert.FromHexString(stored.Key);
        }
        else Identity = Guid.NewGuid().ToString("N");
        Renew();
    }
    public string Renew()
    {
        lock (gate) { Code = RandomNumberGenerator.GetInt32(10000000, 100000000).ToString(); expires = DateTime.UtcNow.AddMinutes(5); attempts = 0; return Code; }
    }
    public object Pair(string code)
    {
        lock (gate)
        {
            if (DateTime.UtcNow > expires || ++attempts > 5 || !Equal(Code, code))
                throw new InvalidOperationException("Código inválido ou expirado. Gere outro no PC.");
            return Issue();
        }
    }
    // Convite embutido no app instalado pelo próprio PC (via SDB, na TV em Developer Mode apontando para este PC):
    // ficha aleatória de uso único, guardada só como hash, válida por tempo limitado e resistente a reinício do auxiliar.
    string InviteFile => Path.Combine(Path.GetDirectoryName(file)!, "install-invite.json");
    public string IssueInvite(TimeSpan ttl) {
        lock (gate) {
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var invite = new Invite(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token))), DateTime.UtcNow.Add(ttl));
            File.WriteAllText(InviteFile + ".tmp", JsonSerializer.Serialize(invite)); File.Move(InviteFile + ".tmp", InviteFile, true);
            inviteAttempts = 0; return token;
        }
    }
    int inviteAttempts;
    public object PairInvite(string token) {
        lock (gate) {
            Invite? invite = null;
            try { invite = JsonSerializer.Deserialize<Invite>(File.ReadAllText(InviteFile)); } catch (Exception e) when (e is IOException or JsonException) { }
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));
            if (invite == null || DateTime.UtcNow > invite.Expires || ++inviteAttempts > 10 || !Equal(invite.Hash, hash))
                throw new InvalidOperationException("Convite de instalação inválido ou expirado. Mostre o código no PC.");
            File.Delete(InviteFile);
            return Issue();
        }
    }
    record Invite(string Hash, DateTime Expires);
    // Só chamado após autorização pelo vínculo independente de recuperação.
    public object RecoverPairing() { lock (gate) return Issue(); }
    object Issue() {
        var next = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new Stored(Identity, Convert.ToHexStringLower(next))));
        File.Move(file + ".tmp", file, true);
        key = next; expires = DateTime.MinValue; nonces.Clear();
        return new { identity = Identity, key = Convert.ToHexStringLower(key) };
    }
    public string Proof(string challenge)
    {
        lock (gate) return key == null ? "" : Sign("discover\n" + challenge + "\n" + Identity, key);
    }
    public bool Verify(string method, string path, string body, string time, string nonce, string signature)
    {
        lock (gate)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (key == null || !long.TryParse(time, out var timestamp) || Math.Abs((double)now - timestamp) > 30000 ||
                nonce.Length is < 16 or > 100 || nonce.Contains('\n')) return false;
            foreach (var old in nonces.Where(n => n.Value < now - 60000).Select(n => n.Key).ToList()) nonces.Remove(old);
            var expected = Sign($"{method}\n{path}\n{time}\n{nonce}\n{body}", key);
            if (!Equal(expected, signature) || !nonces.TryAdd(nonce, now)) return false;
            return true;
        }
    }
    public static string Sign(string text, byte[] secret) => Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(text)));
    public static bool Equal(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    record Stored(string Identity, string Key);
}
