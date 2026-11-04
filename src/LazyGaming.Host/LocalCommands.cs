using System.Security.Cryptography;
using System.Text.Json;

namespace LazyGaming;

// Local OS user only: opening PC windows must not depend on pairing a TV.
public sealed class LocalCommands
{
    readonly byte[] key;
    readonly Dictionary<string,long> nonces = [];
    readonly object gate = new();
    public LocalCommands(string directory) {
        var path = Path.Combine(directory,"pc-command.json");
        using var held = new LibraryFileLock(path);
        if (File.Exists(path)) {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            key = Convert.FromHexString(json.RootElement.GetProperty("Key").GetString()!);
            if (key.Length != 32) throw new InvalidDataException("Acesso local inválido.");
        } else {
            key = RandomNumberGenerator.GetBytes(32);
            DurableJson.Write(path,new { Key = Convert.ToHexStringLower(key) });
        }
    }
    public bool Verify(string method, string path, string body, string time, string nonce, string signature) {
        if (method != "POST" || path is not ("/api/hub/open" or "/api/launcher/toggle")) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!long.TryParse(time,out long stamp) || Math.Abs((double)now-stamp)>30000 || nonce.Length is < 16 or > 100) return false;
        lock (gate) {
            foreach (var item in nonces.Where(p=>now-p.Value>60000).Select(p=>p.Key).ToArray()) nonces.Remove(item);
            if (nonces.ContainsKey(nonce) || !Security.Equal(Security.Sign($"{method}\n{path}\n{time}\n{nonce}\n{body}",key),signature)) return false;
            nonces[nonce]=now; return true;
        }
    }
}
