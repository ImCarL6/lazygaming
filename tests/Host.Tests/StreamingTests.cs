using LazyGaming;
using System.Text.Json;

static class StreamingTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var optionsFolder = Path.Combine(Path.GetTempPath(), "lg-windowed-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(optionsFolder);
        var options = "DisplayMode = Borderless\r\nMonitor = 1\r\nResolution = 800x600\r\nWindowPosition = {\"x\":-1920,\"y\":0}\r\nWindowMaximized = false\r\nShadows = 3\r\n";
        var optionsPath = Path.Combine(optionsFolder, "options.ini"); File.WriteAllText(optionsPath, options);
        File.WriteAllText(Path.Combine(optionsFolder, "SDL3.dll"), "fixture");
        var profileGame = new Game("example", "Example", "unused", Path.Combine(optionsFolder, "Example.exe"));
        WindowedOptionsProfile.Prepare(profileGame, optionsFolder, () => new(0, 0, 3840, 2160));
        Check(File.ReadAllText(optionsPath).Contains("DisplayMode = Windowed") && File.ReadAllText(optionsPath).Contains("Resolution = 3840x2160"), "formato SDL reconhecido prepara janela controlável sem depender de ID do jogo");
        Check(File.ReadAllText(optionsPath).Contains("Monitor = 1") && File.ReadAllText(optionsPath).Contains("Shadows = 3"), "perfil só altera apresentação necessária");
        WindowedOptionsProfile.Prepare(profileGame, optionsFolder, () => new(0, 0, 1920, 1080));
        File.WriteAllText(optionsPath, File.ReadAllText(optionsPath).Replace("Shadows = 3", "Shadows = 1"));
        WindowedOptionsProfile.Restore(optionsFolder);
        Check(File.ReadAllText(optionsPath) == options.Replace("Shadows = 3", "Shadows = 1"), "restaurar apresentação original após várias preparações sem desfazer outras opções gráficas");
        Check(WindowedOptionsProfile.Configure(options + "DisplayMode = Windowed\r\n", new(0,0,3840,2160)) == null && WindowedOptionsProfile.Configure("DisplayMode = Borderless", new(0,0,3840,2160)) == null, "formato duplicado ou parcial não é alterado por inferência");
        Directory.Delete(optionsFolder, true);
        var sharedFolder = Path.Combine(Path.GetTempPath(), "lg-shared-stream-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(sharedFolder);
        var controls = new StreamControls(sharedFolder);
        Check(!controls.Online, "preferências salvas não provam presença da TV");
        var preferences = new StreamPreferences(Bitrate: 10, Adaptive: false);
        controls.Change(preferences, "apply");
        bool pendingRefused = false; try { controls.Change(preferences, "stop"); } catch (InvalidOperationException) { pendingRefused = true; }
        Check(pendingRefused, "comando pendente não é sobrescrito por outro cliente");
        var sharedState = JsonSerializer.SerializeToElement(controls.Report(0, true, "4K60"));
        Check(controls.Online, "heartbeat da TV renova presença após reinício do auxiliar sem novo pareamento");
        Check(sharedState.GetProperty("command").ValueKind == JsonValueKind.Object, "heartbeat não confirma comando que a TV não concluiu");
        sharedState = JsonSerializer.SerializeToElement(controls.Report(1, true, "4K60"));
        Check(sharedState.GetProperty("command").ValueKind == JsonValueKind.Null && sharedState.GetProperty("active").GetBoolean(), "confirmação TV compartilha estado e consome comando exato");
        var restoredControls = JsonSerializer.SerializeToElement(new StreamControls(sharedFolder).Snapshot());
        Check(restoredControls.GetProperty("settings").GetProperty("Bitrate").GetInt32() == 10, "qualidade compartilhada persiste ao reiniciar auxiliar");
        bool invalid = false; try { controls.Change(new(Bitrate: 150), null); } catch (InvalidOperationException) { invalid = true; }
        Check(invalid, "preferência compartilhada inválida rejeitada");
        var inactive = new Displays.Screen("SAMSUNG", "tv", 5, true, false, 3840, 2160, 60);
        Check(SunshineHost.CaptureSource(true, () => inactive, _ => throw new Exception("não ler modo de fonte inativa")) == null, "Stream prepara vínculo antes da ativação sem confundir EDID com modo capturado");
        Check(SunshineHost.CaptureSource(true, () => inactive with { Active = true }, tv => { Check(tv, "captura da TV identificada"); return new() { Width = 3840, Height = 2160 }; })?.Width == 3840, "após preparar sessão, Stream lê resolução ativa real");
        var launchArguments = GameDisplaySettings.RiskLaunchArguments(7, 3840, 2160);
        var qhd=LaunchDisplay.Resolve("odyssey",[new("physical-qhd","odyssey",new(0,0,2560,1440)),new("physical-tv","tv",new(2560,0,3840,2160))],["odyssey","tv"]);
        Check(qhd.Monitor==1 && qhd.Bounds.Width==2560 && GameDisplaySettings.RiskLaunchArguments(qhd.Monitor,qhd.Bounds.Width,qhd.Bounds.Height).Contains("1440"),"launch uses current QHD target independently of other 4K display");
        Check(LaunchDisplay.Resolve("odyssey",[new("qhd","odyssey",new(0,0,2560,1440))],["tv","odyssey"]).Monitor==2,"fresh monitor order follows topology changes");
        bool missingMonitor=false;try{LaunchDisplay.Resolve("odyssey",[new("qhd","odyssey",new(0,0,2560,1440))],["tv"]);}catch(InvalidOperationException){missingMonitor=true;}
        Check(missingMonitor,"never silently launch on monitor one when target index is missing");
        Check(launchArguments.SequenceEqual(new[] { "-applaunch", "632360", "-monitor", "7", "-screen-fullscreen", "0", "-popupwindow", "-screen-width", "3840", "-screen-height", "2160" }), "Risk usa Steam com destino dinâmico e janela sem borda, sem mudar o principal");
        var now = DateTime.UtcNow;
        var focusTarget = new GameWindowTarget("focus-game.exe", now.AddSeconds(90));
        int focusAttempts = 0; bool focused = false;
        for (int i = 0; i < 6; i++) focusTarget.Update([new(7,"focus-game.exe")],now,_=>{focusAttempts++;return focused;});
        focused = true;
        for (int i = 0; i < 16; i++) focusTarget.Update([new(7,"focus-game.exe")],now.AddSeconds(i),_=>{focusAttempts++;return focused;});
        Check(focusAttempts == 19, "posição sem foco não conclui preparação; foco confirmado encerra tentativas limitadas");
        uint inputStamp = 100;
        var lateWindow = new GameWindowTarget("qt-game.exe", now.AddSeconds(90), () => inputStamp);
        int startupCalls = 0;
        for (int i = 0; i <= 14; i++) lateWindow.Update([new(8,"qt-game.exe")], now.AddSeconds(i), _ => { startupCalls++; return true; });
        Check(lateWindow.Phase == "watching", "foco inicial não encerra observação antes de o emulador terminar de abrir");
        lateWindow.Update([new(8,"qt-game.exe")], now.AddSeconds(30), _ => { startupCalls++; return false; });
        lateWindow.Update([new(8,"qt-game.exe")], now.AddSeconds(31), _ => { startupCalls++; return true; });
        Check(startupCalls == 17, "perda tardia/minimização sem nova entrada pode ser reparada dentro do prazo");
        inputStamp++;
        lateWindow.Update([new(8,"qt-game.exe")], now.AddSeconds(32), _ => throw new Exception("não disputar escolha manual do usuário"));
        Check(lateWindow.Phase == "finished" && lateWindow.Error == null, "entrada manual encerra restauração automática");
        var bounded = new GameWindowTarget("qt-game.exe", now.AddSeconds(90), () => 100);
        for (int i = 0; i <= 14; i++) bounded.Update([new(8,"qt-game.exe")], now.AddSeconds(i), _ => true);
        bounded.Update([new(8,"qt-game.exe")], now.AddSeconds(90), _ => throw new Exception("prazo não autoriza disputa contínua"));
        Check(bounded.Phase == "finished" && bounded.Error == null, "foco entregue encerra vigilância no prazo máximo");
        var placement = new GameWindowTarget("risk.exe", now.AddSeconds(90));
        int calls = 0;
        placement.Update([new(1, "steam.exe")], now, _ => { calls++; return true; });
        placement.Update([new(1, "risk.exe"), new(2, "risk.exe")], now, _ => { calls++; return true; });
        Check(calls == 0, "não mover launcher, outro processo ou destino ambíguo");
        placement.Update([new(3, "RISK.exe")], now, _ => { calls++; return false; });
        for (int i = 0; i < 16; i++) placement.Update([new(3, "RISK.exe")], now.AddSeconds(i), _ => { calls++; return true; });
        Check(calls == 14 && placement.Error == null, "aguarda janela, confirma destino estável e para de reposicionar");
        var timedOut = new GameWindowTarget("risk.exe", now);
        timedOut.Update([new(3, "risk.exe")], now, _ => throw new Exception("não mover depois do prazo"));
        Check(timedOut.Error != null, "destino sem confirmação produz erro funcional após prazo");
        timedOut.Cancel("steam.exe"); Check(timedOut.Error != null, "outro processo não limpa falha do jogo");
        timedOut.Cancel("risk.exe"); Check(timedOut.Error == null, "encerrar jogo cancela conferência pendente e limpa erro");
        var wilds = "[Display]\r\nDeviceInstanceName=primary\r\nDisplayName=Odyssey\r\nNormalWindowResolution=(2560,1440)\r\nWindowMode=Fullscreen\r\nFullScreenMode=true\r\nTextureQuality=High\r\n";
        var configured = GameDisplaySettings.ConfigureWilds(wilds, @"\\.\DISPLAY7", 3840, 2160);
        Check(configured.Contains(@"DeviceInstanceName=\\.\DISPLAY7") && configured.Contains("DisplayName=SAMSUNG") && configured.Contains("NormalWindowResolution=(3840,2160)"), "Wilds seleciona destino físico resolvido, independente de monitor principal ou número 3");
        Check(configured.Contains("WindowMode=Borderless") && configured.Contains("FullScreenMode=false") && configured.Contains("TextureQuality=High"), "apresentação na TV preserva qualidade gráfica");
        Check(GameDisplaySettings.ConfigureWilds(configured, @"\\.\DISPLAY7", 3840, 2160) == configured, "configuração de destino idempotente");
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=stream-test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var publicOnly = SunshineHost.ReadPublicCertificate(generated.ExportCertificatePem());
        Check(!publicOnly.HasPrivateKey && publicOnly.Thumbprint == generated.Thumbprint, "pinning usa apenas certificado público, sem procurar chave privada no PEM");
        var cable = new SimulatedEnvironment();
        var transport = new TransportEnvironment(cable);
        transport.Prepare("stream"); transport.Tick();
        Check(transport.Active && cable.Active && cable.Preparations == 1, "Stream prepara destino do jogo");
        transport.Prepare("stream"); Check(cable.Preparations == 1, "retomar Stream é idempotente");
        bool refused = false;
        try { transport.Prepare("cable"); } catch (InvalidOperationException) { refused = true; }
        Check(refused && transport.Active, "troca de transporte não altera sessão ativa");
        refused = false;
        transport.SetMonitors(true);
        Check(transport.DisplayControls.Available, "controle de monitores disponível no Stream");
        refused = false;
        try { transport.ManualAudio("speaker"); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "Stream não troca saída padrão do Windows");
        transport.Restore(); Check(!transport.Active && cable.Restorations == 1, "finalizar Stream restaura sessão");
        transport.Prepare(); Check(cable.Preparations == 2, "Cabo mantém preparação existente");
        transport.Restore(); Check(cable.Restorations == 2, "Cabo mantém restauração existente");
        var folder = Path.Combine(Path.GetTempPath(), "lg-stream-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        new TransportEnvironment(new SimulatedEnvironment(), folder).Prepare("stream");
        var recovered = new TransportEnvironment(new SimulatedEnvironment(), folder);
        Check(recovered.Active && JsonSerializer.Serialize(recovered.Status).Contains("stream"), "reinício mantém transporte sem assumir HDMI");
        recovered.Restore();
        using var clients = JsonDocument.Parse("{\"named_certs\":[{\"uuid\":\"tv\",\"enabled\":true},{\"uuid\":\"revoked\",\"enabled\":false}]}");
        Check(SunshineHost.HasClient(clients.RootElement, "tv") && !SunshineHost.HasClient(clients.RootElement, "other") && !SunshineHost.HasClient(clients.RootElement, "revoked"), "Stream exige cliente exato habilitado");
        Presentation.Validate("stream", true);
        foreach (var json in new[] { "null", "[]", "{\"named_certs\":{}}", "{\"named_certs\":[null,1,{}, {\"uuid\":123}, {\"uuid\":\"tv\",\"enabled\":\"true\"}]}" }) {
            using var malformed = JsonDocument.Parse(json);
            Check(!SunshineHost.HasClient(malformed.RootElement, "tv"), "resposta malformada não habilita Stream nem lança erro");
        }
        Console.WriteLine("PASS: Stream isolado do HDMI, retomada, bloqueios e identidade exata do cliente.");
    }
}
