using System.Security.Cryptography;
using System.Text.Json;
namespace LazyGaming;

// Uma TV confiável. Vínculo independente do código/segredo rotativo da API.
// Enrolamento exige HMAC válido da TV; recuperação exige este outro segredo.
public sealed class PairingRecovery
{
    sealed record Credential(string Id, string Key);
    readonly string file;
    Credential? credential;
    readonly object gate = new();
    readonly Dictionary<string, long> used = [];
    public PairingRecovery(string directory) {
        file = Path.Combine(directory, "tv-recovery.json");
        if (File.Exists(file)) {
            try { var saved = JsonSerializer.Deserialize<Credential>(File.ReadAllText(file)); if (saved != null && Hex(saved.Id, 32) && Hex(saved.Key, 64)) credential = saved; }
            catch (JsonException) { /* Sem credencial válida, não recuperar por inferência. */ }
        }
    }
    static bool Hex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit);
    public object Enroll(string? currentId) { lock (gate) {
        if (credential == null || credential.Id != currentId) {
            var next = new Credential(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(next)); File.Move(file + ".tmp", file, true);
            credential = next; used.Clear();
        }
        return new { id = credential.Id, key = credential.Key };
    } }
    public object Discover(string id, string challenge, string identity) { lock (gate) {
        if (credential == null || !Security.Equal(id, credential.Id) || !Hex(challenge, 32)) return new { available = false };
        return new { available = true, identity, proof = Security.Sign($"recovery-discover\n{challenge}\n{identity}\n{id}", Convert.FromHexString(credential.Key)) };
    } }
    public object? Restore(Security security, string body, string time, string nonce, string signature) { lock (gate) {
        if (credential == null || body.Length > 1024 || !Hex(nonce, 32) || !long.TryParse(time, out var stamp) || Math.Abs((double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - stamp) > 30000) return null;
        string? id;
        try { using var json = JsonDocument.Parse(body); id = json.RootElement.GetProperty("id").GetString(); } catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
        if (id != credential.Id) return null;
        var key = Convert.FromHexString(credential.Key);
        var expected = Security.Sign($"POST\n/api/recovery/restore\n{time}\n{nonce}\n{body}", key);
        if (!Security.Equal(expected, signature)) return null;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var old in used.Where(p => p.Value < now - 60000).Select(p => p.Key).ToArray()) used.Remove(old);
        if (used.Count >= 1024 || !used.TryAdd(nonce, now)) return null;
        var issued = JsonSerializer.SerializeToElement(security.RecoverPairing());
        string identity = issued.GetProperty("identity").GetString()!, secret = issued.GetProperty("key").GetString()!;
        return new { identity, key = secret, proof = Security.Sign($"recovery-restored\n{nonce}\n{identity}\n{secret}", key) };
    } }
}
