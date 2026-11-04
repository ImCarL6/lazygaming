using System.Text.Json;

namespace LazyGaming;

public static partial class Displays
{
    static string TargetKey(PathInfo p) => $"{p.Target.Adapter.High}:{p.Target.Adapter.Low}:{p.Target.Id}";
    static string SourceKey(PathInfo p) => $"{p.Source.Adapter.High}:{p.Source.Adapter.Low}:{p.Source.Id}";
    static SourceMode SourceOf(Snapshot s, PathInfo p) => p.Source.ModeIndex < s.Modes.Length && s.Modes[p.Source.ModeIndex].Type == 1
        ? s.Modes[p.Source.ModeIndex].Source : throw new InvalidOperationException("Modo de tela salvo indisponível.");
    static ModeInfo TargetOf(Snapshot s, PathInfo p) => p.Target.ModeIndex < s.Modes.Length && s.Modes[p.Target.ModeIndex].Type == 2
        ? s.Modes[p.Target.ModeIndex] : throw new InvalidOperationException("Sinal de tela salvo indisponível.");

    // Planejador puro: não consulta nem muda dispositivos. TV primeiro, com origem/modo intactos.
    public static Snapshot PlanMonitors(Snapshot current, Snapshot original, Snapshot available, PathInfo tv, bool enabled)
    {
        var tvKey = TargetKey(tv);
        if (!current.Paths.Any(p => TargetKey(p) == tvKey)) throw new InvalidOperationException("A sessão da TV não está ativa.");
        var tvSource = SourceOf(current, tv);
        if (tvSource.Position.X != 0 || tvSource.Position.Y != 0 || tvSource.Width == 0 || tvSource.Height == 0 || Rate(tv.Target.Refresh) <= 0)
            throw new InvalidOperationException("Prepare a TV como tela principal antes de alterar os monitores.");
        var paths = new List<PathInfo>(); var modes = new List<ModeInfo>();
        var usedSources = new HashSet<string>();
        void Add(PathInfo path, SourceMode source, ModeInfo target)
        {
            if (!usedSources.Add(SourceKey(path))) throw new InvalidOperationException("As telas disputam a mesma origem de vídeo.");
            path.Flags = 1;
            path.Source.ModeIndex = (uint)modes.Count;
            modes.Add(new ModeInfo { Type = 1, Adapter = path.Source.Adapter, Id = path.Source.Id, Source = source });
            path.Target.ModeIndex = (uint)modes.Count; target.Adapter = path.Target.Adapter; target.Id = path.Target.Id;
            modes.Add(target); paths.Add(path);
        }
        Add(tv, tvSource, TargetOf(current, tv));
        if (enabled) {
            var monitors = original.Paths.Where(p => TargetKey(p) != tvKey).ToArray();
            if (monitors.Select(SourceKey).Distinct().Count() != monitors.Length) throw new InvalidOperationException("Monitores clonados não são suportados neste perfil.");
            var left = monitors.Select(p => SourceOf(original, p).Position.X).DefaultIfEmpty(0).Min();
            var top = monitors.Select(p => SourceOf(original, p).Position.Y).DefaultIfEmpty(0).Min();
            foreach (var saved in monitors) {
                var candidates = available.Paths.Where(p => TargetKey(p) == TargetKey(saved) && p.Target.Available && !usedSources.Contains(SourceKey(p)) &&
                    !current.Paths.Any(a => TargetKey(a) != TargetKey(saved) && SourceKey(a) == SourceKey(p))).ToArray();
                var active = current.Paths.Where(p => TargetKey(p) == TargetKey(saved)).ToArray();
                var candidate = candidates.OrderByDescending(p => active.Any(a => SourceKey(a) == SourceKey(p))).FirstOrDefault();
                if (candidates.Length == 0) throw new InvalidOperationException("Um monitor salvo está desconectado ou sem uma origem de vídeo livre.");
                var source = SourceOf(original, saved);
                // Grupo sempre à direita da TV: ligá-lo nunca muda a origem (0,0) do jogo.
                source.Position = new Point { X = checked((int)tvSource.Width + source.Position.X - left), Y = source.Position.Y - top };
                candidate.Target.Refresh = saved.Target.Refresh;
                candidate.Target.Rotation = saved.Target.Rotation; candidate.Target.Scaling = saved.Target.Scaling;
                candidate.Target.Scanline = saved.Target.Scanline;
                Add(candidate, source, TargetOf(original, saved));
            }
        }
        return new(paths.ToArray(), modes.ToArray());
    }
    static string LayoutSignature(Snapshot s) => JsonSerializer.Serialize(s.Paths.OrderBy(TargetKey).Select(p => new {
        target = TargetKey(p), source = SourceKey(p), mode = SourceOf(s, p), signal = TargetOf(s, p).Target,
        refresh = p.Target.Refresh, p.Target.Rotation, p.Target.Scaling, p.Target.Scanline
    }), new JsonSerializerOptions { IncludeFields = true });
    public static bool SameLayout(Snapshot a, Snapshot b) => LayoutSignature(a) == LayoutSignature(b);
    public static bool WaitForStableLayout(Func<bool> matches, Action pause, int consecutive = 8, int attempts = 24)
    {
        int stable = 0;
        for (int i = 0; i < attempts; i++) {
            bool match;
            try { match = matches(); } catch (InvalidOperationException) { match = false; }
            stable = match ? stable + 1 : 0;
            if (stable >= consecutive) return true;
            pause();
        }
        return false;
    }
    public static bool TvReady(bool requirePrimary = true)
    {
        var tv = Tv(); var s = Capture(); var matches = s.Paths.Where(p => Name(p).DevicePath == tv.DevicePath).ToArray();
        if (matches.Length != 1) return false;
        var source = SourceOf(s, matches[0]);
        return (!requirePrimary || (source.Position.X == 0 && source.Position.Y == 0)) && source.Width == tv.Width && source.Height == tv.Height && Math.Abs(Rate(matches[0].Target.Refresh) - tv.Hz) < .2;
    }
    public static System.Drawing.Rectangle TvBounds()
    {
        var target = Tv().DevicePath; var snapshot = Capture();
        var path = snapshot.Paths.Single(p => Name(p).DevicePath == target);
        var source = SourceOf(snapshot, path);
        return new(source.Position.X, source.Position.Y, (int)source.Width, (int)source.Height);
    }
    public static void ConfigureMonitors(Snapshot original, bool enabled, Action? alignWindows = null)
    {
        var before = Capture(); var tvName = Tv().DevicePath;
        var tv = before.Paths.SingleOrDefault(p => Name(p).DevicePath == tvName);
        var planned = PlanMonitors(before, original, Capture(true), tv, enabled);
        ApplyLayout(before, planned, alignWindows);
    }
    // Preserva coordenadas, modos e monitor principal do desktop salvo.
    public static Snapshot PlanDesktop(Snapshot original, Snapshot available, PathInfo tv, SourceMode tvMode, ModeInfo tvSignal)
    {
        var existing = original.Paths.Where(p => TargetKey(p) == TargetKey(tv)).ToArray();
        if (existing.Length == 1) return new(original.Paths.ToArray(), original.Modes.ToArray());
        var free = available.Paths.Where(p => TargetKey(p) == TargetKey(tv) && p.Target.Available && !original.Paths.Any(o => SourceKey(o) == SourceKey(p))).ToArray();
        if (free.Length == 0) throw new InvalidOperationException("A TV não tem uma origem livre para estender o desktop.");
        tv = free[0]; tv.Flags = 1;
        tvMode.Position = new Point { X = original.Paths.Select(p => checked(SourceOf(original, p).Position.X + (int)SourceOf(original, p).Width)).DefaultIfEmpty(0).Max(), Y = 0 };
        tv.Source.ModeIndex = (uint)original.Modes.Length;
        tv.Target.ModeIndex = (uint)original.Modes.Length + 1;
        tv.Target.Refresh = tvSignal.Target.Signal.VSync;
        // QDC_ALL_PATHS pode devolver PREFERRED (128) para destino inativo.
        // Em modo nativo o Windows aplica IDENTITY (1): solicitar esse valor concreto.
        tv.Target.Scaling = 1;
        tv.Target.Scanline = tvSignal.Target.Signal.Scanline;
        tvSignal.Adapter = tv.Target.Adapter; tvSignal.Id = tv.Target.Id;
        return new([..original.Paths, tv], [..original.Modes,
            new ModeInfo { Type = 1, Adapter = tv.Source.Adapter, Id = tv.Source.Id, Source = tvMode }, tvSignal]);
    }
    // Muda a origem do desktop inteiro: mantém distâncias entre telas, modos e
    // fontes. O snapshot original nunca é editado e continua sendo a restauração.
    public static Snapshot PlanPrimary(Snapshot desktop, PathInfo target)
    {
        var matches = desktop.Paths.Where(p => TargetKey(p) == TargetKey(target)).ToArray();
        if (matches.Length != 1 || desktop.Paths.Select(SourceKey).Distinct().Count() != desktop.Paths.Length)
            throw new InvalidOperationException("O destino principal está ambíguo ou há telas clonadas.");
        var origin = SourceOf(desktop, matches[0]).Position;
        var modes = desktop.Modes.ToArray();
        foreach (var path in desktop.Paths) {
            var source = SourceOf(desktop, path);
            source.Position = new Point { X = checked(source.Position.X - origin.X), Y = checked(source.Position.Y - origin.Y) };
            modes[path.Source.ModeIndex].Source = source;
        }
        return new(desktop.Paths.OrderByDescending(p => TargetKey(p) == TargetKey(target)).ToArray(), modes);
    }
    public static void PrepareDesktop(Snapshot original, Action? alignWindows = null)
    {
        var device = Tv(); var available = Capture(true);
        var tv = available.Paths.First(p => Name(p).DevicePath == device.DevicePath && p.Target.Available);
        var preferred = Preferred(tv);
        var planned = PlanPrimary(PlanDesktop(original, available, tv,
            new SourceMode { Width = preferred.Width, Height = preferred.Height, PixelFormat = 4 },
            new ModeInfo { Type = 2, Target = preferred.Mode }), tv);
        var target = planned.Paths.Single(p => TargetKey(p) == TargetKey(tv));
        var source = SourceOf(planned, target);
        if (source.Width != device.Width || source.Height != device.Height || Math.Abs(Rate(target.Target.Refresh) - device.Hz) > .2)
            throw new InvalidOperationException("A TV precisa estar no modo nativo para preservar este desktop.");
        ApplyLayout(Capture(), planned, alignWindows);
    }
    public static Snapshot RestorePlan(Snapshot original, bool restoreTvState) {
        if (restoreTvState) return original;
        var device = SelectTarget(List(), Preferences().DisplayPath);
        if (device == null) return original; // A disconnected TV never prevents desktop recovery.
        var available = Capture(true);
        var tv = available.Paths.First(p => Name(p).DevicePath == device.DevicePath && p.Target.Available);
        var preferred = Preferred(tv);
        return PlanDesktop(original, available, tv,
            new SourceMode { Width = preferred.Width, Height = preferred.Height, PixelFormat = 4 },
            new ModeInfo { Type = 2, Target = preferred.Mode });
    }
    static void ApplyLayout(Snapshot before, Snapshot planned, Action? alignWindows)
    {
        if (SameLayout(before, planned)) { alignWindows?.Invoke(); return; }
        using var windows = DesktopWindows.Capture();
        // Fixar perfil da sessão no banco do Windows para não recair no perfil PC ao reconectar DP.
        // Sem ALLOW_CHANGES ou NO_OPTIMIZATION: não autorizar reajuste do sinal da TV.
        Check(SetDisplayConfig((uint)planned.Paths.Length, planned.Paths, (uint)planned.Modes.Length, planned.Modes, Supplied | 0x40));
        var evidence = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyGaming");
        Save(planned, Path.Combine(evidence, "display-planned.json"));
        try {
            Check(SetDisplayConfig((uint)planned.Paths.Length, planned.Paths, (uint)planned.Modes.Length, planned.Modes, Supplied | Apply | Persist));
            if (!WaitForStableLayout(() => {
                var actual = Capture(); bool same = SameLayout(actual, planned);
                if (!same) Save(actual, Path.Combine(evidence, "display-mismatch.json"));
                return same;
            }, () => Thread.Sleep(250)))
                throw new InvalidOperationException("A TV não permaneceu estável durante a troca; os monitores não foram confirmados.");
            windows.Restore();
            alignWindows?.Invoke();
        } catch {
            int recovery = SetDisplayConfig((uint)before.Paths.Length, before.Paths, (uint)before.Modes.Length, before.Modes, Supplied | Apply | Persist);
            if (recovery != 0 || !WaitForStableLayout(() => SameLayout(Capture(), before), () => Thread.Sleep(250), 4, 16))
                throw new InvalidOperationException("Não foi possível confirmar a recuperação das telas. Confira o HDMI antes de tentar outra troca.");
            windows.Restore();
            throw;
        }
    }
}
