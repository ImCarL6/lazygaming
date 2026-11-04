using LazyGaming;
using System.Text.Json;

static class DisplayProfileTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static Displays.PathInfo Path(uint target, uint source, uint sourceMode = 0, uint targetMode = 1, uint hz = 60) => new() {
        Flags = 1, Source = new() { Adapter = new() { Low = 1 }, Id = source, ModeIndex = sourceMode },
        Target = new() { Adapter = new() { Low = 1 }, Id = target, ModeIndex = targetMode, Available = true, Refresh = new() { Numerator = hz, Denominator = 1 }, Rotation = 1, Scaling = 1 }
    };
    static Displays.Snapshot Snapshot(params (uint Target, uint Source, uint Width, uint Height, int X, uint Hz)[] entries)
    {
        var paths = new List<Displays.PathInfo>(); var modes = new List<Displays.ModeInfo>();
        foreach (var e in entries) {
            paths.Add(Path(e.Target, e.Source, (uint)modes.Count, (uint)modes.Count + 1, e.Hz));
            modes.Add(new() { Type = 1, Id = e.Source, Adapter = new() { Low = 1 }, Source = new() { Width = e.Width, Height = e.Height, PixelFormat = 4, Position = new() { X = e.X } } });
            modes.Add(new() { Type = 2, Id = e.Target, Adapter = new() { Low = 1 }, Target = new() { Signal = new() { ActiveSize = new() { Width = e.Width, Height = e.Height }, VSync = new() { Numerator = e.Hz, Denominator = 1 } } } });
        }
        return new(paths.ToArray(), modes.ToArray());
    }
    public static void Run()
    {
        var checks = 0;
        Check(!Displays.WaitForStableLayout(() => ++checks < 5, () => { }), "sucesso imediato seguido de perda da TV não confirma troca");
        checks = 0;
        Check(Displays.WaitForStableLayout(() => ++checks > 3, () => { }), "aguardar negociação e estabilidade consecutiva");
        Check(!Displays.WaitForStableLayout(() => throw new InvalidOperationException(), () => { }), "consulta transitória falha sem falso sucesso");
        var tv = Snapshot((3, 0, 3840, 2160, 0, 60));
        var solo = Snapshot((1,0,1920,1080,0,144));
        Check(Displays.SameLayout(solo,Displays.PlanMonitors(solo,solo,solo,solo.Paths[0],true)),"monitor único ligado sem demais telas é no-op");
        Check(Displays.SameLayout(solo,Displays.PlanMonitors(solo,solo,solo,solo.Paths[0],false)),"desligar demais telas preserva monitor único em1080p144");
        var original = Snapshot((1, 0, 1920, 1080, -1920, 60), (2, 1, 2560, 1440, 0, 165));
        var all = new Displays.Snapshot([Path(1, 0), Path(1, 1), Path(1, 2), Path(2, 0), Path(2, 1), Path(2, 2), Path(3, 0)], []);
        var json = JsonSerializer.Serialize(original, new JsonSerializerOptions { IncludeFields = true });
        var enabled = Displays.PlanMonitors(tv, original, all, tv.Paths[0], true);
        Check(enabled.Paths.Length == 3 && enabled.Paths[0].Source.Id == 0 && enabled.Paths[0].Target.Id == 3, "TV conserva origem/destino na extensão");
        Check(enabled.Modes[0].Source.Position.X == 0 && enabled.Modes[0].Source.Position.Y == 0 && enabled.Modes[0].Source.Width == 3840, "TV permanece principal em4K");
        Check(enabled.Paths[0].Target.Refresh.Numerator == 60 && enabled.Paths[2].Target.Refresh.Numerator == 165, "preservar taxas exatas da TV/monitor");
        Check(enabled.Paths.Select(p => p.Source.Id).Distinct().Count() == 3, "origem da TV não é reutilizada por monitores");
        Check(enabled.Modes[2].Source.Position.X == 3840 && enabled.Modes[4].Source.Position.X == 5760, "grupo de monitores à direita sem deslocar TV");
        Check(Displays.SameLayout(enabled, Displays.PlanMonitors(enabled, original, all, enabled.Paths[0], true)), "ligar repetidamente gera no-op");
        Check(Displays.SameLayout(tv, Displays.PlanMonitors(enabled, original, all, enabled.Paths[0], false)), "desligar monitores conserva modo TV exato");
        Check(json == JsonSerializer.Serialize(original, new JsonSerializerOptions { IncludeFields = true }), "snapshot anterior não é modificado pelo planejador");
        var partial = Snapshot((3, 0, 3840, 2160, 0, 60), (2, 1, 2560, 1440, 5760, 165));
        var extended = Displays.PlanMonitors(partial, original, all, partial.Paths[0], true);
        Check(extended.Paths.Single(p => p.Target.Id == 2).Source.Id == 1, "não roubar origem de monitor já ligado");
        var missing = all with { Paths = all.Paths.Where(p => p.Target.Id != 1).ToArray() };
        var desktop = Snapshot((1, 0, 2560, 1440, 0, 165), (2, 1, 1920, 1080, -1920, 60), (3, 2, 3840, 2160, 0, 60));
        desktop.Modes[4].Source.Position.Y = -2160;
        var preserved = Displays.PlanDesktop(desktop, all, desktop.Paths[2], tv.Modes[0].Source, tv.Modes[1]);
        Check(Displays.SameLayout(desktop, preserved), "TV já ativa conserva principal, posições, resoluções e taxas sem reorganizar desktop");
        var desktopAvailable = all with { Paths = [..all.Paths, Path(3, 2)] };
        var extendedDesktop = Displays.PlanDesktop(original, desktopAvailable, tv.Paths[0], tv.Modes[0].Source, tv.Modes[1]);
        var preferredTv = desktopAvailable with { Paths = desktopAvailable.Paths.Select(p => { if (p.Target.Id == 3) p.Target.Scaling = 128; return p; }).ToArray() };
        var concrete = Displays.PlanDesktop(original, preferredTv, tv.Paths[0], tv.Modes[0].Source, tv.Modes[1]);
        Check(concrete.Paths[^1].Target.Scaling == 1 && Displays.SameLayout(concrete, extendedDesktop), "TV inativa usa escala nativa concreta e não gera falso rollback ao Windows normalizar PREFERRED");
        var progressive = tv.Modes[1]; progressive.Target.Signal.Scanline = 1;
        var nativeTv = Displays.PlanDesktop(original, preferredTv, tv.Paths[0], tv.Modes[0].Source, progressive);
        Check(nativeTv.Paths[^1].Target.Scanline == 1, "TV inativa usa varredura progressiva do sinal nativo em vez de UNSPECIFIED");
        Check(extendedDesktop.Paths.Length == 3 && extendedDesktop.Modes[0].Source.Position.X == -1920 && extendedDesktop.Modes[2].Source.Position.X == 0, "TV inativa adicionada sem deslocar monitores do PC");
        Check(extendedDesktop.Modes[4].Source.Position.X == 2560 && extendedDesktop.Paths[2].Source.Id == 2, "nova TV usa origem livre à direita sem substituir principal");
        Check(Displays.SameLayout(original, new Displays.Snapshot(extendedDesktop.Paths[..2], extendedDesktop.Modes[..4])), "modos e sinal originais dos monitores preservados");
        var primary = Displays.PlanPrimary(extendedDesktop, extendedDesktop.Paths.Single(p => p.Target.Id == 3));
        Check(primary.Paths[0].Target.Id == 3 && primary.Modes[4].Source.Position.X == 0, "TV principal antes de abrir qualquer jogo");
        Check(primary.Modes[0].Source.Position.X == -4480 && primary.Modes[2].Source.Position.X == -2560, "translação uniforme preserva a organização física dos monitores");
        Check(primary.Paths.Single(p => p.Target.Id == 2).Target.Refresh.Numerator == 165, "TV principal não reduz taxa do Odyssey");
        Check(Displays.SameLayout(primary, Displays.PlanPrimary(primary, primary.Paths[0])), "principal repetido é no-op");
        Check(Displays.SameLayout(original, new Displays.Snapshot(extendedDesktop.Paths[..2], extendedDesktop.Modes[..4])), "principal de sessão não modifica snapshot original");
        var above = Displays.PlanPrimary(desktop, desktop.Paths[2]);
        Check(above.Modes[4].Source.Position.Y == 0 && above.Modes[0].Source.Position.Y == 2160, "TV acima do desktop mantém arranjo relativo no eixo vertical");
        Check(Displays.SameLayout(desktop, Displays.PlanPrimary(above, above.Paths.Single(p => p.Target.Id == 1))), "translação de volta recupera layout exato quando antiga principal retorna");
        var cloned = desktop with { Paths = desktop.Paths.ToArray() }; cloned.Paths[1].Source = cloned.Paths[0].Source;
        bool cloneRefused = false;
        try { Displays.PlanPrimary(cloned, cloned.Paths[2]); } catch (InvalidOperationException) { cloneRefused = true; }
        Check(cloneRefused, "clone não é convertido em extensão por inferência");
        var oldBounds = new System.Drawing.Rectangle(0, 0, 2560, 1440);
        var oldWork = new System.Drawing.Rectangle(0, 40, 2560, 1400);
        var newBounds = new System.Drawing.Rectangle(-2560, 0, 2560, 1440);
        var newWork = new System.Drawing.Rectangle(-2560, 0, 2560, 1400);
        var window = new System.Drawing.Rectangle(100, 100, 800, 600);
        var moved = DesktopWindows.Translate(window, oldBounds, oldWork, newBounds, newWork);
        Check(moved == new System.Drawing.Rectangle(-2460, 140, 800, 600), "janela permanece no monitor físico compensando taskbar superior");
        Check(DesktopWindows.Translate(moved, newBounds, newWork, oldBounds, oldWork) == window, "janela volta sem deriva de workspace/tamanho");
        var physicalWindow = new System.Drawing.Rectangle(24, 50, 1200, 850);
        Check(DesktopWindows.TranslateVisible(physicalWindow, oldBounds, newBounds) == new System.Drawing.Rectangle(-2536, 50, 1200, 850), "janela normal usa posição física atual sem depender de restore bounds do aplicativo");
        bool refused = false;
        try { Displays.PlanMonitors(tv, original, missing, tv.Paths[0], true); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "monitor desconectado recusa alteração sem inventar modo");
        var movedTv = Snapshot((3, 0, 3840, 2160, 1920, 60)); refused = false;
        try { Displays.PlanMonitors(movedTv, original, all, movedTv.Paths[0], false); } catch (InvalidOperationException) { refused = true; }
        Check(refused, "não reconfigurar TV que deixou de ser principal");
        Console.WriteLine("PASS: perfis puros conservam TV/origem/4K60, modos dos monitores, snapshot e recusam ambiguidades.");
    }
}
