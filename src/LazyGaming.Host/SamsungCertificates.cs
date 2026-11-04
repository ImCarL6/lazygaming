using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LazyGaming;

// Certificado Samsung de TV criado pelo próprio app, no mesmo protocolo da Samsung Certificate Extension:
// o usuário entra na conta Samsung na página oficial (a senha nunca passa pelo LazyGaming); a página devolve um token
// para localhost:4794; o app gera as chaves aqui, envia só os pedidos (CSR) e recebe os certificados de autor e de
// distribuidor (com o DUID da TV). Chaves e senhas ficam no PC do usuário (senhas protegidas pelo Windows/DPAPI).
public sealed record SamsungLogin(string AccessToken, string UserId, string Email);

public sealed class SamsungCertificates(HttpClient http, string folder)
{
    public const string Profile = "LazyGamingTV";
    public const string ClientId = "v285zxnl3h", CallbackPath = "/signin/callback";
    public const int CallbackPort = 4794;
    public static readonly string LoginUrl = "https://account.samsung.com/mobile/account/check.do?serviceID=" + ClientId +
        "&actionID=StartOAuth2&accessToken=Y&redirect_uri=http://localhost:" + CallbackPort + CallbackPath;
    public const string AuthorsUrl = "https://svdca.samsungqbe.com/apis/v3/authors", DistributorsUrl = "https://svdca.samsungqbe.com/apis/v3/distributors";
    public string Folder => folder;
    public string AuthorFile => Path.Combine(folder, "author.p12");
    public string DistributorFile => Path.Combine(folder, "distributor.p12");

    // Espera o retorno da página oficial de login (POST com "code" = JSON). Só aceita conexões locais; 5 minutos de prazo.
    public static async Task<SamsungLogin> WaitLogin(Action<string> openBrowser, CancellationToken cancel, int port = CallbackPort, string? url = null) {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (SocketException) { throw new InvalidOperationException("A porta " + port + " está em uso (o Certificate Manager pode estar aberto). Feche-o e tente de novo."); }
        try {
            openBrowser(url ?? LoginUrl);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            while (true) {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(timeout.Token); }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { throw new InvalidOperationException("O login na conta Samsung não terminou em 5 minutos. Tente de novo."); }
                using (client) {
                    var (path, body) = await ReadRequest(client.GetStream(), timeout.Token);
                    var login = path.StartsWith(CallbackPath, StringComparison.Ordinal) ? ParseCallback(body) : null;
                    await Respond(client.GetStream(), login != null ? "Login concluído. Volte ao LazyGaming." : "Aguardando o login da Samsung…", timeout.Token);
                    if (login != null) return login;
                }
            }
        } finally { listener.Stop(); }
    }
    static async Task<(string Path, string Body)> ReadRequest(NetworkStream stream, CancellationToken cancel) {
        var buffer = new byte[65536]; int total = 0, headerEnd = -1, length = 0;
        while (total < buffer.Length) {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancel); if (read == 0) break; total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            if (headerEnd < 0 && (headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal)) >= 0) {
                var m = Regex.Match(text[..headerEnd], @"(?im)^content-length:\s*(\d+)"); length = m.Success ? int.Parse(m.Groups[1].Value) : 0;
            }
            if (headerEnd >= 0 && total >= headerEnd + 4 + length) break;
        }
        var all = Encoding.UTF8.GetString(buffer, 0, total);
        var first = all.Split("\r\n", 2)[0].Split(' ');
        return (first.Length > 1 ? first[1] : "", headerEnd >= 0 ? all[(headerEnd + 4)..] : "");
    }
    static async Task Respond(NetworkStream stream, string message, CancellationToken cancel) {
        var html = "<!doctype html><meta charset=utf-8><title>LazyGaming</title><body style=\"font:18px system-ui;background:#0b1019;color:#eee;display:grid;place-items:center;height:100vh;margin:0\">" + WebUtility.HtmlEncode(message) + "</body>";
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancel); await stream.WriteAsync(body, cancel);
    }
    // Corpo application/x-www-form-urlencoded; campo code = JSON {access_token, userId, inputEmailID}.
    public static SamsungLogin? ParseCallback(string body) {
        foreach (var pair in body.Split('&')) {
            var kv = pair.Split('=', 2); if (kv.Length != 2 || kv[0] != "code") continue;
            try {
                using var doc = JsonDocument.Parse(WebUtility.UrlDecode(kv[1]));
                string S(string n) => doc.RootElement.TryGetProperty(n, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";
                var token = S("access_token"); var user = S("userId");
                if (token.Length > 0 && user.Length > 0) return new(token, user, S("inputEmailID"));
            } catch (JsonException) { }
        }
        return null;
    }

    // Pedido de certificado (PEM), RSA 2048 + SHA-512 como a extensão oficial.
    public static (RSA Key, string Csr) Request(string subject, string? duid) {
        var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
        if (duid != null) request.CertificateExtensions.Add(DeviceNames(duid));
        return (key, PemEncoding.WriteString("CERTIFICATE REQUEST", request.CreateSigningRequest()));
    }

    // subjectAltName com as URIs exatamente como a extensão oficial escreve (maiúsculas preservadas).
    public static X509Extension DeviceNames(string duid) {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (writer.PushSequence()) {
            var uri = new System.Formats.Asn1.Asn1Tag(System.Formats.Asn1.TagClass.ContextSpecific, 6);
            writer.WriteCharacterString(System.Formats.Asn1.UniversalTagNumber.IA5String, "URN:tizen:packageid=", uri);
            writer.WriteCharacterString(System.Formats.Asn1.UniversalTagNumber.IA5String, "URN:tizen:deviceid=" + duid, uri);
        }
        return new X509Extension("2.5.29.17", writer.Encode(), false);
    }
    async Task<X509Certificate2> Submit(string url, SamsungLogin login, string csr, IEnumerable<(string, string)> extra, string fileName, CancellationToken cancel) {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(login.AccessToken), "access_token");
        form.Add(new StringContent(login.UserId), "user_id");
        form.Add(new StringContent("VD"), "platform");
        foreach (var (name, value) in extra) form.Add(new StringContent(value), name);
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csr)), "csr", fileName);
        using var response = await http.PostAsync(url, form, cancel);
        var text = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ExplainError((int)response.StatusCode, text));
        return FirstCertificate(text) ?? throw new InvalidOperationException("A Samsung respondeu sem certificado. Tente de novo mais tarde.");
    }
    public static X509Certificate2? FirstCertificate(string text) {
        var m = Regex.Match(text, "-----BEGIN CERTIFICATE-----[\\s\\S]+?-----END CERTIFICATE-----");
        try { return m.Success ? X509Certificate2.CreateFromPem(m.Value) : null; } catch (CryptographicException) { return null; }
    }
    public static string ExplainError(int status, string body) {
        string? description = null;
        try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("description", out var d)) description = d.ToString(); } catch (JsonException) { }
        if (status == 429) return "A Samsung recusou por excesso de pedidos. Espere alguns minutos e tente de novo.";
        if (status is 401 or 403) return "A Samsung recusou o login" + (description != null ? ": " + description : ". Entre de novo na conta.");
        return "A Samsung recusou o pedido (" + status + ")" + (description != null ? ": " + description : ".");
    }

    // Cria (ou reaproveita) o autor e cria o distribuidor desta TV. O autor é o mesmo para sempre: perder a chave
    // impede atualizar o app já instalado, por isso nunca é recriado quando já existe.
    public async Task<(string AuthorPassword, string DistributorPassword)> Create(SamsungLogin login, string duid, CancellationToken cancel) {
        if (!Regex.IsMatch(duid, "^[A-Za-z0-9._-]{6,64}$")) throw new InvalidOperationException("DUID da TV inválido.");
        Directory.CreateDirectory(folder);
        var name = Regex.Replace(login.Email.Split('@')[0], "[^A-Za-z0-9._-]", ""); if (name.Length == 0) name = "LazyGaming";
        string authorPassword;
        if (File.Exists(AuthorFile)) authorPassword = ReadSecret("author")
            ?? throw new InvalidOperationException("O certificado de autor já existe, mas a senha protegida não pôde ser recuperada. Restaure a reserva do certificado e da senha neste usuário do Windows. O autor existente foi preservado.");
        else {
            var (key, csr) = Request("CN=" + name, null);
            using (key) {
                var signed = await Submit(AuthorsUrl, login, csr, [], "author.csr", cancel);
                authorPassword = NewPassword();
                Write(AuthorFile, Pkcs12(signed, key, Ca("vd_tizen_dev_author_ca.cer"), authorPassword));
                SaveSecret("author", authorPassword);
            }
        }
        var subject = "CN=TizenSDK" + (login.Email.Contains('@') ? ", E=" + login.Email : "");
        var (dkey, dcsr) = Request(subject, duid);
        using (dkey) {
            var signed = await Submit(DistributorsUrl, login, dcsr, [("privilege_level", "Public"), ("developer_type", "Individual")], "distributor.csr", cancel);
            var distributorPassword = NewPassword();
            Write(DistributorFile, Pkcs12(signed, dkey, Ca("vd_tizen_dev_public2.crt"), distributorPassword));
            SaveSecret("distributor", distributorPassword);
            return (authorPassword, distributorPassword);
        }
    }
    public static X509Certificate2 Ca(string file) => X509CertificateLoader.LoadCertificateFromFile(Path.Combine(AppContext.BaseDirectory, "SamsungCA", file));
    // PKCS#12 com cadeia e cifragem 3DES/SHA1 (compatível com o assinador Java da Tizen CLI).
    public static byte[] Pkcs12(X509Certificate2 signed, RSA key, X509Certificate2 ca, string password) {
        using var withKey = signed.CopyWithPrivateKey(key);
        var collection = new X509Certificate2Collection { withKey, ca };
        return collection.ExportPkcs12(Pkcs12ExportPbeParameters.Pkcs12TripleDesSha1, password)!;
    }
    static void Write(string file, byte[] data) { var temp = file + ".tmp"; File.WriteAllBytes(temp, data); File.Move(temp, file, true); }
    // Senhas só com letras e números: vão como argumento fixo da Tizen CLI.
    static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    string SecretFile(string name) => Path.Combine(folder, name + ".pwd");
    void SaveSecret(string name, string value) => File.WriteAllBytes(SecretFile(name), ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    public string? ReadSecret(string name) {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(SecretFile(name)), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException) { return null; }
    }
    public static string DefaultFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SamsungCertificate", Profile);
}
