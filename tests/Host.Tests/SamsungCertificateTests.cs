using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LazyGaming;

// Certificado Samsung sem rede real: servidor Samsung falso que assina os pedidos com uma CA de teste.
static class SamsungCertificateTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    sealed class FakeSamsung : HttpMessageHandler {
        public readonly List<(string Url, Dictionary<string, string> Fields)> Calls = [];
        public int Status = 200;
        readonly RSA caKey = RSA.Create(2048);
        readonly X509Certificate2 ca;
        public FakeSamsung() { var r = new CertificateRequest("CN=Teste CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); r.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true)); ca = r.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(5)); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) {
            var fields = new Dictionary<string, string>();
            foreach (var part in (MultipartFormDataContent)request.Content!) fields[part.Headers.ContentDisposition!.Name!.Trim('"')] = await part.ReadAsStringAsync(cancel);
            Calls.Add((request.RequestUri!.ToString(), fields));
            if (Status != 200) return new((HttpStatusCode)Status) { Content = new StringContent("""{"error":{"name":"x","status":"401","code":"1","description":"token expirado"}}""") };
            var csr = CertificateRequest.LoadSigningRequestPem(fields["csr"], HashAlgorithmName.SHA512, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions, RSASignaturePadding.Pkcs1);
            csr.CertificateExtensions.Clear();
            var signed = csr.Create(ca, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1), RandomNumberGenerator.GetBytes(8));
            return new(HttpStatusCode.OK) { Content = new StringContent(signed.ExportCertificatePem()) };
        }
    }

    public static async Task Run() {
        var json = WebUtility.UrlEncode("""{"access_token":"tok123","userId":"u42","inputEmailID":"carl@example.com","access_token_expires_in":"3600"}""");
        var login = SamsungCertificates.ParseCallback("state=x&code=" + json)!;
        Assert(login.AccessToken == "tok123" && login.UserId == "u42" && login.Email == "carl@example.com", "retorno do login lido");
        Assert(SamsungCertificates.ParseCallback("code=" + WebUtility.UrlEncode("{}")) == null && SamsungCertificates.ParseCallback("x=1") == null, "retorno sem token recusado");
        Assert(SamsungCertificates.LoginUrl.Contains("serviceID=v285zxnl3h") && SamsungCertificates.LoginUrl.Contains("redirect_uri=http://localhost:4794/signin/callback"), "página oficial com o retorno local da extensão");

        // Retorno local: só a rota certa conclui; a página do navegador recebe confirmação.
        int port;
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        string? page = null;
        var waited = await SamsungCertificates.WaitLogin(url => Task.Run(async () => {
            await Task.Delay(100);
            using var http = new HttpClient();
            await http.PostAsync($"http://127.0.0.1:{port}/outra", new StringContent("code=" + json, Encoding.UTF8, "application/x-www-form-urlencoded"));
            var r = await http.PostAsync($"http://127.0.0.1:{port}/signin/callback", new StringContent("code=" + json, Encoding.UTF8, "application/x-www-form-urlencoded"));
            page = await r.Content.ReadAsStringAsync();
        }), default, port, "about:blank");
        await Task.Delay(200);
        for (int i = 0; i < 50 && page == null; i++) await Task.Delay(50);
        Assert(waited.UserId == "u42" && page != null && page.Contains("Volte ao LazyGaming"), "login recebido pelo retorno local: " + waited.UserId + " | " + page);

        // Pedido do distribuidor com as URIs exatas da extensão oficial.
        var (key, csrPem) = SamsungCertificates.Request("CN=TizenSDK, E=carl@example.com", "KLCDNLDVDORTK");
        var parsed = CertificateRequest.LoadSigningRequestPem(csrPem, HashAlgorithmName.SHA512, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        var san = parsed.CertificateExtensions.First(e => e.Oid!.Value == "2.5.29.17").RawData;
        Assert(Encoding.ASCII.GetString(san).Contains("URN:tizen:deviceid=KLCDNLDVDORTK") && Encoding.ASCII.GetString(san).Contains("URN:tizen:packageid="), "SAN com deviceid/packageid exatos");
        Assert(key.KeySize == 2048 && parsed.SubjectName.Name.Contains("CN=TizenSDK"), "RSA 2048 e CN=TizenSDK");
        key.Dispose();

        var root = Path.Combine(Path.GetTempPath(), "lg-samsungcert-" + Guid.NewGuid().ToString("N"));
        try {
            var fake = new FakeSamsung();
            var certs = new SamsungCertificates(new HttpClient(fake), root);
            var (authorPassword, distributorPassword) = await certs.Create(login, "KLCDNLDVDORTK", default);
            Assert(fake.Calls.Count == 2 && fake.Calls[0].Url == SamsungCertificates.AuthorsUrl && fake.Calls[1].Url == SamsungCertificates.DistributorsUrl, "autor e distribuidor pedidos nas URLs v3");
            Assert(fake.Calls.All(c => c.Fields["platform"] == "VD" && c.Fields["access_token"] == "tok123" && c.Fields["user_id"] == "u42"), "campos comuns da extensão (VD, token, usuário)");
            Assert(fake.Calls[1].Fields["privilege_level"] == "Public" && fake.Calls[1].Fields["developer_type"] == "Individual", "distribuidor Public/Individual");
            var author = X509CertificateLoader.LoadPkcs12CollectionFromFile(certs.AuthorFile, authorPassword);
            var dist = X509CertificateLoader.LoadPkcs12CollectionFromFile(certs.DistributorFile, distributorPassword);
            Assert(author.Count == 2 && author.Any(c => c.HasPrivateKey) && author.Any(c => c.Subject.Contains("Samsung VD Author CA")), "autor .p12 com chave e CA de TV");
            Assert(dist.Count == 2 && dist.Any(c => c.HasPrivateKey) && dist.Any(c => c.Subject.Contains("VD DEVELOPER Public CA")), "distribuidor .p12 com chave e CA de TV");
            Assert(certs.ReadSecret("author") == authorPassword && !File.ReadAllText(Path.Combine(root, "author.pwd"), Encoding.Latin1).Contains(authorPassword), "senha guardada protegida pelo Windows");
            Assert(authorPassword.All(char.IsLetterOrDigit), "senha só com letras e números");
            // Outra TV: mesmo autor (necessário para atualizar o app), só um distribuidor novo.
            var authorBytes = File.ReadAllBytes(certs.AuthorFile);
            var again = await certs.Create(login, "OUTRATV1234", default);
            Assert(fake.Calls.Count == 3 && fake.Calls[2].Url == SamsungCertificates.DistributorsUrl && again.AuthorPassword == authorPassword && File.ReadAllBytes(certs.AuthorFile).SequenceEqual(authorBytes), "autor reaproveitado");
            var passwordFile = Path.Combine(root, "author.pwd"); var protectedPassword = File.ReadAllBytes(passwordFile);
            foreach (var missing in new[] { true, false }) {
                if (missing) File.Delete(passwordFile); else File.WriteAllText(passwordFile, "corrompido");
                int calls = fake.Calls.Count; bool preserved = false;
                try { await certs.Create(login, "OUTRATV1234", default); } catch (InvalidOperationException e) { preserved = e.Message.Contains("preservado"); }
                Assert(preserved && fake.Calls.Count == calls && File.ReadAllBytes(certs.AuthorFile).SequenceEqual(authorBytes), "senha ausente/corrompida nunca recria nem substitui autor");
            }
            File.WriteAllBytes(passwordFile, protectedPassword);
            // Erro da Samsung explicado, sem gravar distribuidor novo.
            fake.Status = 401; var before = File.ReadAllBytes(certs.DistributorFile); string? error = null;
            try { await certs.Create(login, "KLCDNLDVDORTK", default); } catch (InvalidOperationException e) { error = e.Message; }
            Assert(error != null && error.Contains("token expirado") && File.ReadAllBytes(certs.DistributorFile).SequenceEqual(before), "erro da Samsung explicado sem perder o certificado anterior");
            bool badDuid = false; try { await certs.Create(login, "x;rm", default); } catch (InvalidOperationException) { badDuid = true; }
            Assert(badDuid, "DUID inválido recusado");
            Assert(SamsungCertificates.ExplainError(429, "").Contains("excesso"), "limite de pedidos explicado");

            // O assinador da Tizen CLI é Java: o keytool do JDK local precisa abrir o .p12.
            var keytool = Path.Combine(Environment.GetEnvironmentVariable("LAZYGAMING_JDK") ?? @"C:\Users\carlo\Documents\Codex\2026-10-23\cim\work\tizen\sdk\jdk", "bin", "keytool.exe");
            if (File.Exists(keytool)) {
                var psi = new System.Diagnostics.ProcessStartInfo(keytool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in new[] { "-list", "-keystore", certs.AuthorFile, "-storetype", "pkcs12", "-storepass", authorPassword }) psi.ArgumentList.Add(a);
                using var p = System.Diagnostics.Process.Start(psi)!; var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
                Assert(p.ExitCode == 0 && output.Contains("PrivateKeyEntry"), "Java (keytool) abre o .p12: " + output);
                Console.WriteLine("PASS: keytool do JDK Tizen abre o .p12 gerado.");
            }
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine("PASS: certificado Samsung — login local, CSR com DUID, autor/distribuidor VD, .p12 com cadeia, senha protegida, autor reaproveitado, erros explicados.");
    }
}
