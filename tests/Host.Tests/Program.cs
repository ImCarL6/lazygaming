using LazyGaming;

if (args.Contains("--process-lifetime-fixture")) { await Task.Delay(1000); Environment.Exit(259); return; }
if (args.Contains("--runtime-identity-fixture")) { await Task.Delay(30000); return; }
if (args.Contains("--verify-runtime-identity")) { RuntimeIdentityTests.Run(); return; }
if (args.Length == 3 && args[0] == "--monitor-preference-fixture") {
    new TvSession(args[1], new FakeOutput()).SetMonitors(bool.Parse(args[2]));
    Console.WriteLine("saved"); Console.Out.Flush();
    await Task.Delay(Timeout.Infinite); return;
}

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
var inactiveTv = new Displays.Screen("SAMSUNG", "physical-tv", 5, true, false, 3840, 2160, 60);
Assert(Presentation.Cable([inactiveTv]).Available == true, "TV conectada inativa continua elegível a Cabo");
Assert(Presentation.Cable([inactiveTv with { Available = false }]).Available == false, "TV desconectada não oferece Cabo");
Assert(Presentation.Cable([inactiveTv, inactiveTv with { DevicePath = "other-tv" }]).Available == null, "destino ambíguo não escolhe TV");
bool streamRejected = false;
try { Presentation.Validate("stream"); } catch (InvalidOperationException) { streamRejected = true; }
Assert(streamRejected, "Stream não configurado é rejeitado antes de preparar sessão");
Presentation.Validate("cable");
DisplayProfileTests.Run();
await DisplayPreferenceTests.Run();
StreamingTests.Run();
PairingRecoveryTests.Run();
RuntimeIdentityTests.Run();
ControllerMenuTests.Run();
InputFocusHandoffTests.Run();
LauncherOpeningFocusTests.Run();
TaskbarOverflowTests.Run();
NativeDesktopTests.Run();
await LibraryMetadataTests.Run();
await LocalGameTests.Run();
await LaunchIsolationTests.Run();
await ProcessExitTests.Run();
using (var dormantBackground = new GameBackground(() => throw new Exception("launcher oculto não consulta jogo"), () => false)) {
    Assert(await dormantBackground.Frame() == null, "captura desativada com launcher oculto");
    await dormantBackground.Tick();
}
var steamRoots = Path.Combine(Path.GetTempPath(), "lg-steam-roots-" + Guid.NewGuid().ToString("N"));
var realSteam = Path.Combine(steamRoots, "Steam"); var fakeSteam = Path.Combine(steamRoots, "Mycopunk");
Directory.CreateDirectory(Path.Combine(realSteam, "steamapps")); Directory.CreateDirectory(fakeSteam);
File.WriteAllText(Path.Combine(realSteam, "steam.exe"), "fixture"); File.WriteAllText(Path.Combine(realSteam, "steamapps", "libraryfolders.vdf"), "fixture");
Assert(GameLibrary.ChooseSteam([fakeSteam, realSteam]) == realSteam, "registro apontando para launcher local não elimina Steam real");
Directory.Delete(steamRoots, true);
var nav = new NativeNavigation(); var navReport = new byte[78]; navReport[0] = 1; navReport[1] = navReport[2] = 128; navReport[5] = 2;
Assert(nav.Update(navReport,0,true).SequenceEqual([39]), "seta direita no Bluetooth básico");
Assert(nav.Repeated.Count == 0, "primeiro toque não é repetição");
Assert(nav.Update(navReport,100,true).Count == 0 && nav.Update(navReport,300,true).SequenceEqual([39]), "repetição de navegação controlada");
Assert(nav.Repeated.SetEquals([39]), "repetição HID identificada para não atravessar etapas de metadados");
navReport[5] = 8; nav.Update(navReport,310,true); navReport[5] = 4;
Assert(nav.Update(navReport,320,true).SequenceEqual([40]) && nav.Repeated.Count == 0, "primeiro Baixo é toque novo");
Assert(nav.Update(navReport,620,true).SequenceEqual([40]) && nav.Repeated.SetEquals([40]), "Baixo mantido é repetição");
navReport[5] = 8; nav.Update(navReport,630,true); navReport[5] = 4;
Assert(nav.Update(navReport,640,true).SequenceEqual([40]) && nav.Repeated.Count == 0, "segundo toque após soltar é novo");
var queuedKeys = new ControllerKeys(); queuedKeys.Push(40, true);
Assert(System.Text.Json.JsonSerializer.Serialize(await queuedKeys.Take(CancellationToken.None)).Contains("\"repeat\":true"), "fila preserva repetição até o frontend");
navReport[5] = 0x28; Assert(nav.Update(navReport,400,true).SequenceEqual([13]), "X confirma");
Assert(nav.Update(navReport,500,true).Count == 0 && nav.Update(navReport,600,false).Count == 0, "confirmação não repete nem atua fora do painel");
var basic = new byte[78]; basic[0] = 1; basic[6] = 0x30;
Assert(DualSenseReport.LauncherChord(basic), "Create/Options no Bluetooth básico preenchido até78 bytes");
Assert(DualSenseReport.MicrophonePressed(basic) == null, "básico não inventa microfone");
var chord = new LauncherHold(); Assert(!chord.Update(true, 0) && chord.Update(true, 1000) && !chord.Update(true, 2000), "combinação dispara uma vez por hold");
var clickReport = new byte[78]; clickReport[0] = 1; clickReport[7] = 2;
Assert(DualSenseReport.TouchpadPressed(clickReport), "clique físico no relatório básico");
var clickFull = new byte[78]; clickFull[0] = 0x31; clickFull[11] = 2;
Assert(DualSenseReport.TouchpadPressed(clickFull), "clique físico Bluetooth completo");
var localDesktop = new DesktopLauncher(47985, Path.GetTempPath());
localDesktop.SetInputClient("current-client-12345");
Assert(localDesktop.OwnsInput("current-client-12345") && !localDesktop.OwnsInput("hidden-client-12345"), "somente janela atual recebe controle");
var tap = new DoubleTap();
Assert(!tap.Update(true, 0) && !tap.Update(false, 80) && !tap.Update(true, 200) && tap.Update(false, 260), "duplo toque abre launcher");
Assert(!tap.Update(true, 300) && !tap.Update(false, 1900), "hold não abre launcher");
tap.Reset(); Assert(!tap.Update(true, 2100) && !tap.Update(false, 2160) && !tap.Update(true, 3000) && !tap.Update(false, 3060), "toques separados não abrem launcher");
var shared = new LauncherState();
var initial = System.Text.Json.JsonSerializer.Serialize(await shared.Wait(-1, default));
Assert(initial.Contains("\"revision\":0"), "estado inicial compartilhado");
var waiting = shared.Wait(0, default); shared.Select("wilds");
Assert(System.Text.Json.JsonSerializer.Serialize(await waiting).Contains("wilds"), "seleção acorda o outro cliente");
Assert(ProcessImage.PathOf(Environment.ProcessId) != null, "consultar imagem com acesso limitado");
Assert(WindowsGames.RuntimeMatches(Environment.ProcessId, Path.GetFileName(ProcessImage.PathOf(Environment.ProcessId)!)), "validar consulta WMI real da linha de comando do runtime");
var steamFixture = Path.Combine(Path.GetTempPath(), "lazygaming-library-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(steamFixture, "steamapps", "common", "Example"));
File.WriteAllText(Path.Combine(steamFixture, "steamapps", "common", "Example", "Example.exe"), "fixture, never executed");
File.WriteAllText(Path.Combine(steamFixture, "steamapps", "appmanifest_111.acf"), "\"AppState\" { \"appid\" \"111\" \"name\" \"Example\" \"installdir\" \"Example\" \"StateFlags\" \"4\" }");
File.WriteAllText(Path.Combine(steamFixture, "steamapps", "appmanifest_112.acf"), "\"AppState\" { \"appid\" \"112\" \"name\" \"Missing\" \"installdir\" \"Missing\" \"StateFlags\" \"4\" }");
var fixtureLibrary = new GameLibrary(steamFixture, false, steamFixture, []);
Assert(fixtureLibrary.Games.Any(g => g.Id == "steam-111" && g.Name == "Example") && !fixtureLibrary.Games.Any(g => g.Id == "steam-112"), "indexar manifesto instalado e excluir pasta inexistente");
Directory.CreateDirectory(Path.Combine(steamFixture, "steamapps", "common", "Example", "bin"));
File.WriteAllText(Path.Combine(steamFixture, "steamapps", "common", "Example", "bin", "Game.exe"), "fixture");
Directory.CreateDirectory(Path.Combine(steamFixture, "appcache"));
string[] strings = ["appinfo", "common", "name", "type", "config", "launch", "0", "executable"];
using (var blob = new MemoryStream())
{
    using var writer = new BinaryWriter(blob, System.Text.Encoding.UTF8, true);
    void Node(uint key) { writer.Write((byte)0); writer.Write(key); }
    void Value(uint key, string text) { writer.Write((byte)1); writer.Write(key); writer.Write(System.Text.Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
    Node(0); Node(1); Value(2, "Example from Steam metadata"); Value(3, "Game"); writer.Write((byte)8);
    Node(4); Node(5); Node(6); Value(7, "bin/Game.exe"); for (int i = 0; i < 5; i++) writer.Write((byte)8);
    var payload = blob.ToArray();
    using var file = new BinaryWriter(File.Create(Path.Combine(steamFixture, "appcache", "appinfo.vdf")));
    file.Write(0x07564429u); file.Write(1u); file.Write(16L + 8 + 60 + payload.Length + 4);
    file.Write(111u); file.Write((uint)(60 + payload.Length)); file.Write(new byte[60]); file.Write(payload); file.Write(0u);
    file.Write((uint)strings.Length); foreach (var text in strings) { file.Write(System.Text.Encoding.UTF8.GetBytes(text)); file.Write((byte)0); }
}
fixtureLibrary.Refresh();
Assert(fixtureLibrary.MetadataCount == 1 && fixtureLibrary.Games.Single(g => g.Id == "steam-111").Name == "Example from Steam metadata", "parsear appinfo v41 com tabela de strings");
Assert(fixtureLibrary.Games.Single(g => g.Id == "steam-111").ProcessPath.EndsWith(@"bin\Game.exe"), "usar executável da configuração Steam em vez de chute por nome");
var existingGame = fixtureLibrary.Games.Single(g => g.Id == "steam-111");
var runningPlatform = new SimulatedGames(); runningPlatform.Active.Add(new(123, existingGame.ProcessPath));
var resumeEnvironment = new SimulatedEnvironment();
var resumeService = new GameService(fixtureLibrary, runningPlatform, resumeEnvironment);
await GameMoveTests.Run(Assert);
await resumeService.Start(existingGame.Id);
Assert(resumeEnvironment.Active && resumeEnvironment.Preparations == 1 && runningPlatform.LaunchCount == 0, "jogo já aberto prepara TV sem iniciar outra instância");
var streamCable = new SimulatedEnvironment(); var streamEnvironment = new TransportEnvironment(streamCable);
var afterVideoPlatform = new SimulatedGames(); afterVideoPlatform.Active.Add(new(42, existingGame.ProcessPath));
var afterVideoEnvironment = new SimulatedEnvironment();
var afterVideoGames = new GameService(fixtureLibrary, afterVideoPlatform, afterVideoEnvironment);
await afterVideoGames.FocusRunning();
Assert(afterVideoPlatform.Targeted.SequenceEqual(new[] { existingGame.Id }) && afterVideoPlatform.LaunchCount == 0, "vídeo conectado revalida foco do jogo existente sem lançar outra instância");
Assert(afterVideoEnvironment.DesktopReconfirmations == 1 && afterVideoEnvironment.Preparations == 0, "conexão do Stream reconfirma apps nas telas originais antes do jogo sem trocar topologia");
foreach (var catalogGame in fixtureLibrary.Games.Where(g => File.Exists(g.ProcessPath))) {
    foreach (var transportMode in new[] { "stream", "cable" }) {
        var allGamesPlatform = new SimulatedGames();
        var allGamesService = new GameService(fixtureLibrary, allGamesPlatform, new SimulatedEnvironment());
        await allGamesService.Start(catalogGame.Id, transportMode);
        await allGamesService.Start(catalogGame.Id, transportMode);
        Assert(allGamesPlatform.Targeted.SequenceEqual(new[] { catalogGame.Id, catalogGame.Id }) && allGamesPlatform.LaunchCount == 1, "todo jogo recebe destino ao abrir/continuar em Cabo e Stream sem duplicação");
    }
}
var streamPlatform = new SimulatedGames(); var streamGames = new GameService(fixtureLibrary, streamPlatform, streamEnvironment);
await streamGames.Start(existingGame.Id, "stream"); await streamGames.Start(existingGame.Id, "stream");
Assert(streamPlatform.LaunchCount == 1 && streamPlatform.LastMode == "stream" && streamCable.Preparations == 1, "Stream prepara TV e retoma sem duplicar");
try { await streamGames.Start(existingGame.Id, "cable"); throw new Exception("troca silenciosa de transporte"); } catch (InvalidOperationException) { }
Assert(streamEnvironment.Active && streamPlatform.LaunchCount == 1, "troca recusada preserva jogo/sessão Stream");
var unityRoot = Path.Combine(steamFixture, "NonSteam"); Directory.CreateDirectory(Path.Combine(unityRoot, "Runner_Data"));
File.WriteAllText(Path.Combine(unityRoot, "Runner_Data", "app.info"), "Studio\nNome real do jogo");
File.WriteAllText(Path.Combine(unityRoot, "Runner.exe"), "fixture");
Assert(GameLibrary.IdentifyLocalTitle(unityRoot, Path.Combine(unityRoot, "Runner.exe")) == "Nome real do jogo", "identificar não Steam pelos dados locais, não pelo wrapper");
var localFixture = new GameLibrary(steamFixture, false, steamFixture, [unityRoot]);
Assert(localFixture.Games.Any(g => !g.Steam && g.Name == "Nome real do jogo" && g.ProcessPath.EndsWith("Runner.exe")), "indexação não Steam sem nome ou ID codificado");
Assert(GameLibrary.SafeInside(@"C:\games\test", @"bin\game.exe") == @"C:\games\test\bin\game.exe", "executável dentro da instalação");
Assert(GameLibrary.SafeInside(@"C:\games\test", @"..\other.exe") == null, "não sair da instalação por caminho relativo");
Assert(GameLibrary.IsHelper("UnityCrashHandler64.exe") && GameLibrary.IsHelper("TerrariaServer.exe") && !GameLibrary.IsHelper("Terraria.exe"), "separar jogo de servidor e auxiliares");
Assert(WindowsGames.RuntimeCommandMatches("dotnet.exe \"C:\\games\\tModLoader.dll\"", "tModLoader.dll"), "reconhecer runtime específico");
Assert(!WindowsGames.RuntimeCommandMatches("dotnet.exe tModLoader.dll -server", "tModLoader.dll") && !WindowsGames.RuntimeCommandMatches("dotnet.exe tModLoader.dll.bak", "tModLoader.dll"), "não confundir servidor ou outra aplicação");
var originalConfig = "REFrameworkConfig_MenuOpen=true\r\nREFrameworkConfig_RememberMenuState=false\r\nREFrameworkConfig_MenuKey_V2=36\r\nVR_Enabled=false\r\n";
var closedConfig = GameDisplaySettings.CloseReframeworkMenu(originalConfig);
Assert(closedConfig.Contains("REFrameworkConfig_MenuOpen=false\r\n") && closedConfig.Contains("REFrameworkConfig_RememberMenuState=true\r\n"), "REFramework fechado ao iniciar");
Assert(closedConfig.Contains("MenuKey_V2=36\r\nVR_Enabled=false"), "preservar atalho e opções dos mods");
Assert(GameDisplaySettings.CloseReframeworkMenu(closedConfig) == closedConfig, "configuração idempotente");
static byte[] Report(bool bluetooth, bool active, int x = 0, int y = 0)
{
    var bytes = new byte[bluetooth ? 78 : 64]; bytes[0] = bluetooth ? (byte)0x31 : (byte)1;
    int start = bluetooth ? 34 : 33; bytes[start] = active ? (byte)7 : (byte)0x87;
    bytes[start + 1] = (byte)x; bytes[start + 2] = (byte)((x >> 8) | ((y & 15) << 4)); bytes[start + 3] = (byte)(y >> 4); bytes[start + 4] = 0x80;
    return bytes;
}
foreach (bool bluetooth in new[] { false, true })
{
    var frame = DualSenseReport.Parse(Report(bluetooth, true, 1730, 850));
    Assert(frame?.First == new TouchPoint(7, 1730, 850) && frame.Second == null, "decodificar USB/BT com offsets corretos");
    Assert(DualSenseReport.Parse(Report(bluetooth, false))?.First == null, "toque inativo");
    var held = Report(bluetooth, false); held[bluetooth ? 11 : 10] = 4;
    Assert(DualSenseReport.MicrophonePressed(held) == true, "ler botão de microfone USB/BT");
}
var toggle = new HoldToggle();
Assert(!toggle.Update(true, 0) && !toggle.Update(true, 1499) && toggle.Update(true, 1500), "exigir 1,5 s antes de alternar");
Assert(!toggle.Update(true, 3000), "segurar não alterna repetidamente");
Assert(!toggle.Update(false, 3100) && !toggle.Update(true, 3200) && toggle.Update(true, 4700), "soltar e segurar novamente alterna uma vez");
toggle.Reset(); Assert(!toggle.Update(true, 5000), "reconexão não herda botão segurado");
Assert(DualSenseReport.Parse(new byte[10]) == null && DualSenseReport.Parse(new byte[78]) == null, "relatório básico não pode gerar cliques");
var gestures = new TouchpadGesture();
Assert(gestures.Update(new(new(1, 100, 100), null), 0).Count == 0, "toque inicial sem salto");
Assert(gestures.Update(new(new(1, 110, 100), null), 50).Single() == new MouseAction("move", 14, 0), "deslizar move ponteiro");
Assert(gestures.Update(new(null, null), 400).Count == 0, "deslizar/longo não clica");
gestures.Update(new(new(1, 100, 100), null), 500);
Assert(gestures.Update(new(null, null), 600).Count == 0, "toque curto não clica");
gestures.Update(new(new(1, 100, 100), new(2, 200, 100)), 700);
Assert(gestures.Update(new(null, null), 800).Count == 0, "dois dedos não clicam sem pressão física");
gestures.Update(new(new(1, 100, 100), null), 900); gestures.Reset();
Assert(gestures.Update(new(null, null), 950).Count == 0, "perda de foco/sessão não gera clique pendente");
var directory = Path.Combine(Path.GetTempPath(), "lazygaming-session-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var output = new FakeOutput();
var session = new TvSession(directory, output);
session.Prepare();
Assert(session.Active && output.Prepared, "preparar a tela antes do jogo");
Assert(output.Selected.Count == 0, "não selecionar saída inexistente");
output.Audio.Add(new("tv", "SAMSUNG (NVIDIA High Definition Audio)", false));
session.Tick();
Assert(output.Selected.SequenceEqual(["tv"]), "áudio HDMI que apareceu depois deve ser selecionado");
session.Tick();
Assert(output.Selected.Count == 1, "não repetir troca de áudio já confirmada");
output.Audio.RemoveAll(d => d.Id == "tv"); session.Tick();
output.Audio.Add(new("tv-new-id", "SAMSUNG (NVIDIA High Definition Audio)", false)); session.Tick();
Assert(output.Selected.Last() == "tv-new-id", "redescobrir endpoint após reconexão HDMI");
session.ManualAudio("speaker"); session.Tick();
Assert(output.Selected.Last() == "speaker", "escolha manual de speaker deve prevalecer durante a sessão");
session.Restore();
Assert(!session.Active && output.Restored && output.AudioRestored, "restaurar tela e papéis de áudio originais");
Assert(!File.Exists(Path.Combine(directory, "session.json")), "remover recuperação só após sucesso");
output.Fail = true;
try { session.Prepare(); throw new Exception("erro de tela ignorado"); }
catch (InvalidOperationException) { Assert(!session.Active, "falha de preparação desfaz sessão"); }
Console.WriteLine("PASS: REFramework fechado/preservação, relatórios USB/BT e gestos sem cliques indevidos; sessão, áudio tardio/reconectado, override manual, restauração e rollback.");
var monitorDirectory = Path.Combine(Path.GetTempPath(), "lg-monitors-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(monitorDirectory);
var monitorOutput = new FakeOutput(); var monitorSession = new TvSession(monitorDirectory, monitorOutput);
monitorSession.SetMonitors(true);
Assert(monitorOutput.MonitorChanges == 0 && !monitorOutput.Prepared, "escolha fora da sessão não toca nas telas");
monitorSession.Prepare();
var changes = monitorOutput.MonitorChanges; var audioChanges = monitorOutput.Selected.Count;
monitorSession.SetMonitors(true);
Assert(monitorOutput.MonitorChanges == changes, "pedido repetido não reconfigura monitores");
monitorSession.SetMonitors(false);
Assert(monitorOutput.MonitorChanges == changes + 1 && monitorOutput.Selected.Count == audioChanges, "alternar monitores não seleciona áudio");
monitorOutput.MonitorFailure = true;
try { monitorSession.SetMonitors(true); throw new Exception("perfil incompatível aceito"); } catch (InvalidOperationException) { }
Assert(!monitorSession.DisplayControls.MonitorsEnabled, "falha não confirma perfil novo");
monitorSession.ManualAudio("speaker");
var recovered = new TvSession(monitorDirectory, monitorOutput); audioChanges = monitorOutput.Selected.Count; recovered.Tick();
Assert(monitorOutput.Selected.Count == audioChanges && !recovered.DisplayControls.MonitorsEnabled, "retomada preserva perfil e override de áudio");
monitorOutput.MonitorFailure = false; monitorSession.Restore();
Assert(monitorOutput.Restored && !monitorSession.Active, "encerrar restaura snapshot original");
Console.WriteLine("PASS: preferência sem efeitos, sessão idempotente, falha sem confirmação, áudio e restauração preservados.");
monitorSession.SetMonitors(true); monitorSession.Prepare(); monitorOutput.Prepared = false;
Assert(!monitorSession.DisplayControls.Available, "TV perdida não anuncia perfil disponível");
try { monitorSession.SetMonitors(true); throw new Exception("no-op aceitou TV perdida"); } catch (InvalidOperationException) { }
monitorSession.Prepare();
Assert(monitorOutput.Prepared && !monitorSession.DisplayControls.MonitorsEnabled, "Continuar recupera somente TV sem repetir perfil instável");

foreach (bool enabled in new[] { false, true }) {
    var streamDirectory = Path.Combine(Path.GetTempPath(), "lg-stream-display-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(streamDirectory);
    var streamOutput = new FakeOutput();
    var streamSession = new TvSession(streamDirectory, streamOutput);
    var transport = new TransportEnvironment(streamSession, streamDirectory);
    transport.SetMonitors(enabled);
    Assert(streamOutput.MonitorChanges == 0, "preferência Stream sem sessão não toca telas");
    transport.Prepare("stream");
    Assert(streamOutput.Prepared && transport.DisplayControls.MonitorsEnabled == enabled && streamOutput.Selected.Count == 0, "Stream prepara TV e respeita monitores sem trocar áudio Windows");
    transport.SetMonitors(!enabled);
    Assert(transport.DisplayControls.MonitorsEnabled == !enabled, "Stream permite alternar monitores na sessão");
    var resumed = new TransportEnvironment(new TvSession(streamDirectory, streamOutput), streamDirectory);
    Assert(System.Text.Json.JsonSerializer.Serialize(resumed.Status).Contains("stream"), "retomada reconhece sessão Stream com snapshot");
    resumed.Restore();
    Assert(!resumed.Active && streamOutput.Restored && streamOutput.AudioRestored, "Stream restaura snapshot ao encerrar");
}

sealed class FakeOutput : ISessionPlatform
{
    public bool Prepared, Restored, AudioRestored, Fail;
    public List<AudioDevice> Audio = [new("speaker", "EDIFIER", true)];
    public List<string> Selected = [];
    public void ValidateTv() { }
    public void PrepareDesktop(Displays.Snapshot original) { if (Fail) throw new InvalidOperationException("tela indisponível"); ConfigureMonitors(original, true); Prepared = true; }
    public Displays.Snapshot CaptureDisplays() => new([], []);
    public void PrepareTv() { if (Fail) throw new InvalidOperationException("tela indisponível"); Prepared = true; }
    public bool TvReady() => Prepared;
    public int MonitorChanges;
    public bool MonitorFailure;
    public void ConfigureMonitors(Displays.Snapshot original, bool enabled) { if (MonitorFailure) throw new InvalidOperationException("perfil recusado"); MonitorChanges++; }
    public void RestoreDisplays(Displays.Snapshot snapshot) { Restored = true; Prepared = false; }
    public List<AudioDevice> ListAudio() => Audio.ToList();
    public string[] CaptureAudio() => ["console", "media", "communications"];
    public void SelectAudio(string id)
    {
        Selected.Add(id);
        Audio = Audio.Select(d => d with { Current = d.Id == id }).ToList();
    }
    public void RestoreAudio(string[] ids) { if (!ids.SequenceEqual(["console", "media", "communications"])) throw new Exception("papéis perdidos"); AudioRestored = true; }
}
