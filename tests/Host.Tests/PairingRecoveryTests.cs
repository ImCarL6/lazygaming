using LazyGaming;
using System.Text.Json;
using System.Security.Cryptography;
static class PairingRecoveryTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    public static void Run() {
        var directory = Path.Combine(Path.GetTempPath(), "lg-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            var security = new Security(directory); var initial = Json(security.Pair(security.Code));
            var recovery = new PairingRecovery(directory); var link = Json(recovery.Enroll(null));
            string id = link.GetProperty("id").GetString()!, secret = link.GetProperty("key").GetString()!;
            Check(Json(recovery.Enroll(id)).GetProperty("key").GetString() == secret, "reabrir a mesma TV não troca seu vínculo de recuperação");
            security.Pair(security.Renew()); // Código novo invalida API anterior, mas não recuperação.
            var stored = new PairingRecovery(directory);
            var challenge = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            var proof = Json(stored.Discover(id, challenge, security.Identity));
            Check(proof.GetProperty("proof").GetString() == Security.Sign($"recovery-discover\n{challenge}\n{security.Identity}\n{id}", Convert.FromHexString(secret)), "mesmo PC comprova vínculo independente após troca de código e reinício");
            string body = JsonSerializer.Serialize(new { id }), time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            string signature = Security.Sign($"POST\n/api/recovery/restore\n{time}\n{nonce}\n{body}", Convert.FromHexString(secret));
            Check(stored.Restore(security, body, time, nonce, new string('0',64)) == null, "não recuperar sem prova do segredo independente");
            var result = Json(stored.Restore(security, body, time, nonce, signature)!);
            string key = result.GetProperty("key").GetString()!;
            Check(result.GetProperty("proof").GetString() == Security.Sign($"recovery-restored\n{nonce}\n{security.Identity}\n{key}", Convert.FromHexString(secret)), "resposta autentica novo segredo e nonce exato");
            Check(stored.Restore(security, body, time, nonce, signature) == null, "pedido de recuperação só pode ser usado uma vez");
            Check(Json(stored.Discover("wrong", challenge, security.Identity)).GetProperty("available").GetBoolean() == false, "outro PC/vínculo não recebe prova");
            File.Delete(Path.Combine(directory, "pairing.json"));
            var recreated = new Security(directory);
            Check(Json(stored.Discover(id, challenge, recreated.Identity)).GetProperty("available").GetBoolean(), "perda só do pareamento principal preserva recuperação separada");
            stored.Enroll(null);
            Check(!Json(stored.Discover(id, challenge, recreated.Identity)).GetProperty("available").GetBoolean(), "novo enrolamento confiável substitui vínculo da TV anterior");
        } finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: vínculo independente, prova do PC, persistência, rotação, resposta autenticada, replay e revogação.");
    }
}
