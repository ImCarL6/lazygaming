using LazyGaming;

// Instalação automática do app da TV sem TV real: cada condição e cada falha com executor falso.
static class TvInstallerTests
{
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    const string Applist = """
--------------app_id                                =CidaoLive1.Live-------------
--------------app_version                           =1.0.0-------------
---------------------------------------------------------------------------------------------
--------------app_id                                =LazyGame01.LazyGaming-------------
--------------app_index                             =309-------------
--------------app_version                           =0.17.0-------------
--------------app_tizen_id                          =LazyGame01.LazyGaming-------------
""";
    const string Info = """{"device":{"OS":"Tizen","type":"Samsung SmartTV","modelName":"UN58CU7700GXZD","name":"[TV] Sala","developerMode":"1","developerIP":"192.168.0.10","id":"uuid:abc"}}""";

    sealed class FakeRunner(Func<string, IReadOnlyList<string>, ToolResult> respond) : IToolRunner {
        public readonly List<string> Calls = [];
        public Task<ToolResult> Run(string file, IReadOnlyList<string> args, TimeSpan timeout, IDictionary<string, string>? env, CancellationToken cancel) {
            Calls.Add(Path.GetFileName(file) + " " + string.Join(" ", args)); return Task.FromResult(respond(file, args));
        }
    }

    public static async Task Run() {
        // Valida as dependências reais da UI, além das fixtures de instalação: arquivo
        // novo referenciado no HTML precisa entrar também na lista do pacote da TV.
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "tv", "index.html"))) repo = repo.Parent;
        Assert(repo != null, "fontes da interface encontradas para validar o pacote");
        var index = File.ReadAllText(Path.Combine(repo!.FullName, "tv", "index.html"));
        foreach (System.Text.RegularExpressions.Match script in System.Text.RegularExpressions.Regex.Matches(index, "<script src=\"([^\"$:/]+)\""))
            Assert(TvInstaller.Files.Contains(script.Groups[1].Value), "dependência real da UI incluída no pacote TV: " + script.Groups[1].Value);
        Assert(TvInstaller.InstalledVersion(Applist) == "0.17.0", "versão instalada lida do bloco certo");
        Assert(TvInstaller.InstalledVersion(Applist.Replace("LazyGame01.LazyGaming", "Outro.App")) == null, "sem app LazyGaming não há versão");
        Assert(TvInstaller.Newer("0.27.0", "0.17.0") && !TvInstaller.Newer("0.27.0", "0.27.0") && !TvInstaller.Newer("0.27.0", "0.30.0") && TvInstaller.Newer("0.27.0", null), "comparação de versões");
        var tv = TvInstaller.ParseInfo("192.168.0.14", Info)!;
        Assert(tv.DeveloperMode && tv.DeveloperIp == "192.168.0.10" && tv.Model == "UN58CU7700GXZD" && tv.Name == "[TV] Sala", "API 8001 lida");
        Assert(TvInstaller.ParseInfo("1.2.3.4", """{"device":{"OS":"Android","type":"x"}}""") == null, "não Samsung/Tizen ignorado");
        Assert(TvInstaller.ActiveProfile("""<profiles active="B"><profile name="A"/><profile name="B"/></profiles>""") == "B", "perfil ativo");
        Assert(TvInstaller.ActiveProfile("""<profiles active="X"><profile name="A"/></profiles>""") == "A", "perfil ativo inexistente usa o primeiro");
        Assert(TvInstaller.ActiveProfile("<profiles/>") == null, "sem perfis");
        Assert(TvInstaller.Subnets([System.Net.IPAddress.Parse("192.168.0.10"), System.Net.IPAddress.Parse("26.1.2.3")]).Count() == 253, "varre só a sub-rede privada, sem o próprio PC");
        Assert(TvInstaller.ExplainInstall("Install failed. author signature mismatch").Contains("assinatura"), "erro de assinatura explicado");

        var root = Path.Combine(Path.GetTempPath(), "lg-tvinst-" + Guid.NewGuid().ToString("N"));
        try {
            // Ferramentas no layout padrão e no layout histórico.
            var studio = Path.Combine(root, "tizen-studio");
            foreach (var f in new[] { @"tools\sdb.exe", @"tools\ide\bin\tizen.bat" }) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(studio, f))!); File.WriteAllText(Path.Combine(studio, f), ""); }
            Directory.CreateDirectory(Path.Combine(studio, "jdk"));
            Directory.CreateDirectory(Path.Combine(root, @"tizen-studio-data\profile"));
            File.WriteAllText(Path.Combine(root, @"tizen-studio-data\profile\profiles.xml"), """<profiles active="Meu"><profile name="Meu"/></profiles>""");
            var located = TvInstaller.Locate([Path.Combine(root, "nada"), studio])!;
            Assert(located.Profile == "Meu" && located.JavaHome!.EndsWith("jdk") && located.Cli.EndsWith("tizen.bat"), "Tizen Studio padrão localizado com perfil");
            var legacy = Path.Combine(root, "outra", "legado");
            foreach (var f in new[] { @"sdb\data\tools\sdb.exe", @"sdk\tools\ide\bin\tizen.bat" }) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(legacy, f))!); File.WriteAllText(Path.Combine(legacy, f), ""); }
            Assert(TvInstaller.Locate([legacy]) is { Profile: null }, "layout histórico localizado; sem perfil é informado");

            // Pacote mínimo do app (config + index referenciando arquivos).
            var bundle = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(Path.Combine(bundle, "assets")); Directory.CreateDirectory(Path.Combine(bundle, "stream-engine"));
            foreach (var f in TvInstaller.Files) File.WriteAllText(Path.Combine(bundle, f), "x");
            File.WriteAllText(Path.Combine(bundle, "config.xml"), """<widget version="0.27.0"/>""");
            File.WriteAllText(Path.Combine(bundle, "index.html"), """<link href="style.css"><script src="app.js"></script><script src="$WEBAPIS/webapis/webapis.js"></script>""");
            var stage = Path.Combine(root, "stage"); TvInstaller.Stage(bundle, stage);
            Assert(File.Exists(Path.Combine(stage, "first-run.js")) && !File.Exists(Path.Combine(stage, "hub.html")), "staging só com arquivos do app TV");
            File.WriteAllText(Path.Combine(bundle, "index.html"), """<script src="falta.js"></script>""");
            bool refused = false; try { TvInstaller.Stage(bundle, Path.Combine(root, "stage2")); } catch (InvalidOperationException) { refused = true; }
            Assert(refused, "recurso ausente recusa o pacote");
            File.WriteAllText(Path.Combine(bundle, "index.html"), """<script src="app.js"></script>""");

            var probeInfo = Info;
            TvDevice? Probe(string ip) => ip == "192.168.0.14" ? TvInstaller.ParseInfo(ip, probeInfo) : null;
            string? packagedInvite = null;
            TvInstaller Make(IToolRunner runner, TizenTools? tools) => new(bundle, root, () => tools, runner, () => ["192.168.0.10"],
                (ip, _) => Task.FromResult(Probe(ip)), () => ["192.168.0.13", "192.168.0.14"], () => "{\"identity\":\"pc1\",\"invite\":\"abc\"}");
            var installedVersion = "0.17.0";
            var runner = new FakeRunner((file, args) => {
                if (args[0] == "connect") return new(0, "connected to 192.168.0.14:26101", false);
                if (args.Contains("vd_applist")) return new(0, Applist.Replace("0.17.0", installedVersion), false);
                if (args[0] == "package") { var o = args[args.ToList().IndexOf("-o") + 1]; packagedInvite = File.ReadAllText(Path.Combine(args[^1], "pairing.js")); File.WriteAllText(Path.Combine(o, "LazyGaming.wgt"), "wgt"); return new(0, "Package File Location: x", false); }
                if (args[0] == "install") { installedVersion = "0.27.0"; return new(0, "Installing...\ninstall completed", false); }
                return new(0, "", false);
            });

            // Sem adesão: só avalia, nunca instala.
            var s = await Make(runner, located).Check(false, false, null, default);
            Assert(s.Phase == "ready" && !runner.Calls.Any(c => c.Contains("install") || c.Contains("package")), "sem adesão não instala: " + s.Phase);
            // Com adesão: empacota com o perfil, instala com alvo explícito e confirma a versão pela TV.
            runner.Calls.Clear();
            s = await Make(runner, located).Check(true, false, null, default);
            Assert(s.Phase == "installed" && s.InstalledVersion == "0.27.0", "instalou e confirmou: " + s.Phase + " " + s.Message);
            Assert(runner.Calls.Any(c => c.StartsWith("tizen.bat package -t wgt -s Meu -o ")) && runner.Calls.Any(c => c == "tizen.bat install -n LazyGaming.wgt -s 192.168.0.14:26101 -- " + runner.Calls.First(x => x.Contains("install -n")).Split(" -- ")[1]), "argumentos fixos de empacotar/instalar");
            Assert(runner.Calls.Any(c => c == "tizen.bat run -p LazyGame01.LazyGaming -s 192.168.0.14:26101"), "abre o app depois de instalar");
            Assert(packagedInvite == "window.LG_BOOTSTRAP = {\"identity\":\"pc1\",\"invite\":\"abc\"};\n", "convite de pareamento embutido só no pacote instalado: " + packagedInvite);
            Assert(File.ReadAllText(Path.Combine(bundle, "pairing.js")) == "x", "convite não altera os arquivos do PC");
            Assert(!runner.Calls.Any(c => c.Contains("uninstall")), "nunca desinstala");
            Assert(!Directory.EnumerateDirectories(root, "tv-install-*").Any(), "pasta temporária removida");
            // Mesma versão: não reinstala.
            runner.Calls.Clear();
            s = await Make(runner, located).Check(true, false, "192.168.0.14", default);
            Assert(s.Phase == "installed" && !runner.Calls.Any(c => c.Contains("install -n")), "mesma versão não reinstala");

            // Condições falsas, cada uma com mensagem própria e sem tocar nas ferramentas.
            installedVersion = "0.17.0"; runner.Calls.Clear();
            probeInfo = Info.Replace("\"developerIP\":\"192.168.0.10\"", "\"developerIP\":\"192.168.0.18\"");
            s = await Make(runner, located).Check(true, false, null, default);
            Assert(s.Phase == "developer-ip" && s.Message!.Contains("192.168.0.10") && runner.Calls.Count == 0, "Developer Mode com outro IP orienta e não conecta");
            probeInfo = Info.Replace("\"developerMode\":\"1\"", "\"developerMode\":\"0\"");
            Assert((await Make(runner, located).Check(true, false, null, default)).Phase == "developer-off", "Developer Mode desligado");
            probeInfo = Info;
            Assert((await Make(runner, null).Check(true, false, null, default)).Phase == "tools-missing", "ferramentas ausentes");
            Assert((await Make(runner, located with { Profile = null }).Check(true, false, null, default)).Phase == "profile-missing", "perfil ausente");
            var refusing = new FakeRunner((_, args) => args[0] == "connect" ? new(1, "failed to connect", false) : new(0, "", false));
            Assert((await Make(refusing, located).Check(true, false, null, default)).Phase == "sdb-failed", "SDB recusado");
            Assert((await new TvInstaller(bundle, root, () => located, runner, () => ["192.168.0.10"], (_, _) => Task.FromResult<TvDevice?>(null), () => ["192.168.0.20"]).Check(true, false, null, default)).Phase == "tv-not-found", "sem TV");

            // Falha de assinatura: explica, não repete sozinho em seguida; Tentar agora força.
            var failing = new FakeRunner((file, args) => {
                if (args[0] == "connect") return new(0, "connected to", false);
                if (args.Contains("vd_applist")) return new(0, Applist, false);
                if (args[0] == "package") { var o = args[args.ToList().IndexOf("-o") + 1]; File.WriteAllText(Path.Combine(o, "LazyGaming.wgt"), "wgt"); return new(0, "", false); }
                if (args[0] == "install") return new(1, "install failed[118, -12] Check certificate error : author signature", false);
                return new(0, "", false);
            });
            var installer = Make(failing, located);
            s = await installer.Check(true, false, null, default);
            Assert(s.Phase == "install-failed" && s.Message!.Contains("DUID"), "falha de certificado explicada: " + s.Message);
            failing.Calls.Clear(); s = await installer.Check(true, false, null, default);
            Assert(!failing.Calls.Any(c => c.Contains("install -n")), "falha igual não repete automaticamente");
            failing.Calls.Clear(); await installer.Check(true, true, null, default);
            Assert(failing.Calls.Any(c => c.Contains("install -n")), "Tentar agora força nova tentativa");

            // Duas TVs: só escolhe sozinho quando exatamente uma aponta para este PC.
            var two = new TvInstaller(bundle, root, () => located, runner, () => ["192.168.0.10"],
                (ip, _) => Task.FromResult(ip == "192.168.0.14" ? TvInstaller.ParseInfo(ip, Info) : ip == "192.168.0.15" ? TvInstaller.ParseInfo(ip, Info.Replace("\"developerMode\":\"1\"", "\"developerMode\":\"0\"")) : null),
                () => ["192.168.0.14", "192.168.0.15"]);
            Assert((await two.Check(false, false, null, default)).Tv?.Ip == "192.168.0.14", "duas TVs: escolhe a que aponta para este PC");
            // Convite de pareamento: uso único, só como hash, expira, resiste a reinício, não aceita ficha errada.
            var pairDir = Path.Combine(root, "pair"); var sec = new Security(pairDir);
            var token = sec.IssueInvite(TimeSpan.FromHours(1));
            Assert(!File.ReadAllText(Path.Combine(pairDir, "install-invite.json")).Contains(token), "ficha guardada só como hash");
            bool Refused(Security s2, string t) { try { s2.PairInvite(t); return false; } catch (InvalidOperationException) { return true; } }
            Assert(Refused(sec, "errada"), "ficha errada recusada");
            var restarted = new Security(pairDir);
            Assert(!restarted.Paired, "sem vínculo antes do convite");
            restarted.PairInvite(token);
            Assert(restarted.Paired && Refused(restarted, token), "convite pareia uma vez e não serve de novo");
            var expiring = new Security(Path.Combine(root, "pair2")); var old = expiring.IssueInvite(TimeSpan.FromMilliseconds(-1));
            Assert(Refused(expiring, old), "convite expirado recusado");
            // Busca de instalação existente: pasta com "tizen" no nome achada em profundidade; AppData ignorada.
            var search = Path.Combine(root, "busca");
            var deep = Path.Combine(search, "Documents", "Codex", "proj", "work", "Meu Tizen");
            foreach (var f in new[] { @"sdb\data\tools\sdb.exe", @"sdk\tools\ide\bin\tizen.bat" }) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(deep, f))!); File.WriteAllText(Path.Combine(deep, f), ""); }
            var decoy = Path.Combine(search, "AppData", "tizen-studio");
            foreach (var f in new[] { @"tools\sdb.exe", @"tools\ide\bin\tizen.bat" }) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(decoy, f))!); File.WriteAllText(Path.Combine(decoy, f), ""); }
            Assert(TvInstaller.Search([search], 9, TimeSpan.FromSeconds(5)) == deep, "busca encontra a instalação fora do padrão e ignora AppData");
            var plain = Path.Combine(search, "Downloads", "pasta", "sdkcopy");
            foreach (var f in new[] { @"tools\sdb.exe", @"tools\ide\bin\tizen.bat" }) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(plain, f))!); File.WriteAllText(Path.Combine(plain, f), ""); }
            Directory.Delete(deep, true);
            Assert(TvInstaller.Search([search], 9, TimeSpan.FromSeconds(5)) == plain, "sem nome tizen, acha pelo sdb.exe");
            Assert(TvInstaller.Search([search], 1, TimeSpan.FromSeconds(5)) == null, "profundidade limitada");

            // Download: tamanho diferente do oficial é recusado e nada é aberto.
            var opened = false;
            var download = new TizenStudioDownload(new HttpClient(new FixedHandler(new byte[1000])), Path.Combine(root, "dl"));
            download.Start(_ => true, _ => opened = true, default);
            for (int i = 0; i < 200 && download.Current.Phase is "idle" or "downloading" or "verifying"; i++) await Task.Delay(20);
            Assert(download.Current.Phase == "failed" && !opened && !File.Exists(download.File), "download incompleto recusado: " + download.Current.Phase);
            Assert(TizenStudioDownload.Url.StartsWith("https://download.tizen.org/"), "origem oficial fixa em HTTPS");
            var official = Path.Combine(Path.GetTempPath(), "lg-tizen-dl", "web-cli_Tizen_Studio_6.1_windows-64.exe");
            if (File.Exists(official)) Assert(Authenticode.SignedBySamsung(official), "instalador oficial com assinatura Samsung válida");
            var unsigned = Path.Combine(root, "nao-assinado.exe"); File.WriteAllText(unsigned, "x");
            Assert(!Authenticode.SignedBySamsung(unsigned), "arquivo sem assinatura recusado");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine("PASS: instalação automática da TV — condições, argumentos fixos, versão confirmada pela TV, falhas explicadas sem repetição e sem desinstalar; busca de ferramentas e download verificado.");
    }
}

sealed class FixedHandler(byte[] body) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
}
