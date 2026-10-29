using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace LazyGaming;

public sealed record StreamAvailability(bool Available, string? Note);
public sealed record StreamTarget(string ApplicationId, string ServerUid, int? AppId);
public interface IStreamHost
{
    Task<StreamAvailability> Inspect();
    Task<StreamTarget> Target();
}

// Só consulta a instância privada, em loopback e com certificado fixado. Não cria serviço/processo.
public sealed class SunshineHost(string directory) : IStreamHost
{
    readonly string configDirectory = Path.Combine(directory, "streaming", "config");
    public const string MoonlightId = "MoonLightS.MoonlightWasm";
    public sealed record Settings(string ServerUid, string ClientUuid, int? AppId = null);
    sealed record Admin(string Username, string Password);
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static bool HasClient(JsonElement root, string uuid) => !string.IsNullOrWhiteSpace(uuid) &&
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("named_certs", out var clients) &&
        clients.ValueKind == JsonValueKind.Array && clients.EnumerateArray().Any(c =>
        c.ValueKind == JsonValueKind.Object && c.TryGetProperty("uuid", out var id) &&
        id.ValueKind == JsonValueKind.String && id.GetString() == uuid &&
        (!c.TryGetProperty("enabled", out var enabled) || enabled.ValueKind == JsonValueKind.True));
    public static X509Certificate2 ReadPublicCertificate(string pem) => X509Certificate2.CreateFromPem(pem);
    HttpClient Client()
    {
        var admin = JsonSerializer.Deserialize<Admin>(File.ReadAllText(Path.Combine(configDirectory, "admin.json")), JsonOptions)
            ?? throw new InvalidOperationException("Preparação local do Stream pendente.");
        using var expected = ReadPublicCertificate(File.ReadAllText(Path.Combine(configDirectory, "credentials", "cacert.pem")));
        var pinned = expected.GetCertHash(HashAlgorithmName.SHA256);
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null && CryptographicOperations.FixedTimeEquals(cert.GetCertHash(HashAlgorithmName.SHA256), pinned) };
        var client = new HttpClient(handler) { BaseAddress = new("https://127.0.0.1:47990"), Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(admin.Username + ":" + admin.Password)));
        return client;
    }
    Settings Load()
    {
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(configDirectory, "integration.json")), JsonOptions);
        if (settings == null || !Guid.TryParse(settings.ServerUid, out _) || !Guid.TryParse(settings.ClientUuid, out _) || settings.AppId is < 0)
            throw new InvalidOperationException("Pareamento do Stream pendente.");
        return settings;
    }
    public object EmbeddedConfiguration()
    {
        var settings = Load();
        using var cert = ReadPublicCertificate(File.ReadAllText(Path.Combine(configDirectory, "credentials", "cacert.pem")));
        using var rsa = cert.GetRSAPublicKey() ?? throw new InvalidOperationException("Certificado do Stream incompatível.");
        var publicKey = "sha256//" + Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
        var config = File.ReadAllLines(Path.Combine(configDirectory, "sunshine.conf"));
        var output = config.FirstOrDefault(l => l.TrimStart().StartsWith("output_name =", StringComparison.Ordinal))?.Split('=', 2)[1].Trim();
        bool tv = false;
        if (!string.IsNullOrEmpty(output)) {
            using var selected = JsonDocument.Parse(File.ReadAllText(Path.Combine(configDirectory, "capture-source.json")));
            if (selected.RootElement.GetProperty("outputId").GetString() != output || selected.RootElement.GetProperty("target").GetString() != "samsung-tv")
                throw new InvalidOperationException("O monitor configurado no Sunshine precisa ser identificado antes do modo automático.");
            tv = true;
        }
        var source = CaptureSource(tv, Displays.Tv, Displays.StreamSource);
        return new { serverUid = settings.ServerUid, certificate = publicKey,
            source = source.HasValue ? new { width = source.Value.Width, height = source.Value.Height } : null, port = 47989 };
    }
    public static Displays.SourceMode? CaptureSource(bool tv, Func<Displays.Screen> television, Func<bool, Displays.SourceMode> capture) =>
        tv && !television().Active ? null : capture(tv);
    public async Task ApproveEmbeddedPair(string pin, System.Net.IPAddress remote)
    {
        if (pin.Length != 4 || pin.Any(c => c < '0' || c > '9')) throw new InvalidOperationException("PIN inválido.");
        using var client = Client();
        for (int attempt = 0; attempt < 10; attempt++) {
            using var response = await client.GetAsync("/api/pin"); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var matches = json.RootElement.GetProperty("pairings").EnumerateArray().Where(p => {
                var address = p.GetProperty("address").GetString();
                return System.Net.IPAddress.TryParse(address, out var ip) && ip.MapToIPv4().Equals(remote.MapToIPv4());
            }).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException("Há mais de um pareamento pendente desta TV.");
            if (matches.Length == 1) {
                var id = matches[0].GetProperty("id").GetString();
                if (id == null || id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c))) throw new InvalidOperationException("Pedido de pareamento inválido.");
                using var result = await client.PostAsJsonAsync("/api/pin", new { pairing_id = id, pin, name = "LazyGaming integrado" });
                using var body = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
                if (!result.IsSuccessStatusCode || body.RootElement.GetProperty("status").ValueKind != JsonValueKind.True)
                    throw new InvalidOperationException("Pareamento da transmissão não confirmado.");
                return;
            }
            await Task.Delay(400);
        }
        throw new InvalidOperationException("A TV não apresentou pedido de pareamento.");
    }
    public async Task ConfirmEmbeddedClient(string certHash)
    {
        if (certHash.Length != 64 || certHash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidOperationException("Certificado do cliente inválido.");
        using var state = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(configDirectory, "sunshine-state.json")));
        var devices = state.RootElement.GetProperty("root").GetProperty("named_devices").EnumerateArray().Where(c =>
            c.GetProperty("name").GetString() == "LazyGaming integrado" &&
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(c.GetProperty("cert").GetString() ?? ""))) == certHash).ToArray();
        if (devices.Length != 1) throw new InvalidOperationException("O certificado integrado precisa ter um único vínculo habilitado no Sunshine.");
        var uuid = devices[^1].GetProperty("uuid").GetString();
        if (!Guid.TryParse(uuid, out _)) throw new InvalidOperationException("Identidade do cliente inválida.");
        using var client = Client(); using var response = await client.GetAsync("/api/clients/list"); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!HasClient(json.RootElement, uuid!)) throw new InvalidOperationException("Cliente integrado não está habilitado.");
        var previous = Load(); var path = Path.Combine(configDirectory, "integration.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(new Settings(previous.ServerUid, uuid!, previous.AppId)));
        File.Move(path + ".tmp", path, true);
    }
    public async Task<StreamAvailability> Inspect()
    {
        try {
            var settings = Load(); using var client = Client();
            using var response = await client.GetAsync("/api/clients/list");
            if (!response.IsSuccessStatusCode) return new(false, "O Sunshine não confirmou acesso local.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return HasClient(body.RootElement, settings.ClientUuid) ? new(true, null) : new(false, "Pareie o Moonlight da TV com este PC.");
        } catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or HttpRequestException or TaskCanceledException or CryptographicException) {
            return new(false, "Sunshine/Moonlight: preparação e pareamento local pendentes.");
        }
    }
    public async Task<StreamTarget> Target()
    {
        if (!(await Inspect()).Available) throw new InvalidOperationException("Stream indisponível: conclua a preparação e o pareamento.");
        var settings = Load(); return new(MoonlightId, settings.ServerUid, settings.AppId);
    }
}

// Cabo e Stream têm ambientes separados. Sair do cliente não encerra o jogo.
public sealed class TransportEnvironment : IGameEnvironment
{
    readonly IGameEnvironment cable;
    readonly string? marker;
    string mode = "cable"; bool streamActive;
    public TransportEnvironment(IGameEnvironment cable, string? directory = null) {
        this.cable = cable;
        marker = directory == null ? null : Path.Combine(directory, "transport-session.json");
        if (marker != null && File.Exists(marker)) { streamActive = true; mode = "stream"; }
    }
    public bool Active => streamActive || cable.Active;
    public object Status {
        get {
            var result = JsonSerializer.SerializeToElement(cable.Status, new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            if (streamActive) result["active"] = true;
            result["mode"] = mode; return result;
        }
    }
    public DisplayControls DisplayControls => cable.DisplayControls;
    public void Prepare(string requested = "cable")
    {
        Presentation.Validate(requested, true);
        if (Active && mode != requested) throw new InvalidOperationException("Encerre a sessão atual antes de trocar entre Cabo e Stream.");
        if (requested == "stream") {
            cable.Prepare("stream");
            if (marker != null) File.WriteAllText(marker, "{\"mode\":\"stream\"}");
            streamActive = true; mode = requested; return;
        }
        cable.Prepare(); mode = requested;
    }
    public void Tick() => cable.Tick();
    public void ReconfirmDesktop() => cable.ReconfirmDesktop();
    public void Restore() {
        if (streamActive) {
            cable.Restore();
            if (cable.Active) return;
            if (marker != null) GameDisplaySettings.Restore(Path.GetDirectoryName(marker)!);
            if (marker != null) File.Delete(marker);
            streamActive = false; mode = "cable";
        }
        else cable.Restore();
    }
    public void ManualAudio(string id) {
        if (streamActive) throw new InvalidOperationException("O áudio do Stream é controlado pelo Moonlight.");
        cable.ManualAudio(id);
    }
    public void SetMonitors(bool enabled) {
        cable.SetMonitors(enabled);
    }
}
