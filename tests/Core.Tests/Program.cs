using LazyGaming;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static byte[] Report(bool bluetooth = false) {
    var report = new byte[bluetooth ? 78 : 64]; report[0] = bluetooth ? (byte)0x31 : (byte)1;
    for (int i = bluetooth ? 2 : 1; i < (bluetooth ? 6 : 5); i++) report[i] = 128;
    report[bluetooth ? 9 : 8] = 8;
    report[bluetooth ? 34 : 33] = report[bluetooth ? 38 : 37] = 0x80;
    return report;
}

IControllerAdapter adapter = new DualSenseAdapter();
foreach (bool bt in new[] { false, true }) {
    var bytes = Report(bt);
    var state = adapter.Decode(bytes)!;
    Check(state.Neutral && state.Microphone == false && state.Touch is { First: null, Second: null }, "USB/BT: capacidades reais");
    bytes[bt ? 11 : 10] = 4;
    Check(adapter.Decode(bytes) is { Neutral: false, Microphone: true }, "microfone decodificado");
    bytes[bt ? 11 : 10] = 2;
    Check(adapter.Decode(bytes)!.TouchpadPressed, "pressão física do touchpad");
}
var basic = new byte[78]; basic[0] = 1; basic[5] = 8;
for (int i = 1; i <= 4; i++) basic[i] = 128;
Check(adapter.Decode(basic) is { Microphone: null, Touch: null, Neutral: true }, "BT básico preenchido não inventa capacidades");
for (int length = 0; length < 10; length++) Check(adapter.Decode(new byte[length]) == null, "relatório truncado recusado");
Check(adapter.Decode(new byte[78]) == null, "ID desconhecido recusado");

var neutral = new ControllerState(ControllerButtons.None, true, false, new(null, null), false, false);
var processor = new ControllerProcessor();
processor.Process(neutral, 0, true, true); processor.Process(neutral, 80, true, true);
var confirm = neutral with { Buttons = ControllerButtons.Confirm, Neutral = false };
Check(processor.Process(confirm, 90, true, true).Navigation.Single().Key == 13, "confirmação normalizada");
Check(processor.Process(confirm, 490, true, true).Navigation.Count == 0, "confirmar mantido não repete");
processor.Process(neutral, 500, true, false);
Check(processor.Process(confirm, 590, true, true).Navigation.Count == 0, "perda de foco exige soltura nova");
processor.Reset(1000);
var mic = neutral with { Microphone = true, Neutral = false };
processor.Process(mic, 1100, true, false);
Check(processor.Process(mic, 2600, true, false).MouseModeChanged == true && processor.Enabled, "hold de 1,5s alterna mouse uma vez");
Check(processor.Process(mic, 2700, true, false).MouseModeChanged == null, "hold não alterna repetidamente");
processor.Process(neutral, 2800, true, false);
var touch = neutral with { Touch = new(new(1, 100, 100), null) };
Check(processor.Process(touch, 2810, true, false).Mouse.Count == 0, "toque leve não clica");
var move = touch with { Touch = new(new(1, 110, 105), null) };
Check(processor.Process(move, 2820, true, false).Mouse.Single() == new MouseAction("move", 14, 7), "gesto produz movimento relativo");
var click = move with { TouchpadPressed = true };
Check(processor.Process(click, 2830, true, false).Mouse.Single().Kind == "left-click", "pressão faz clique único");
Check(processor.Process(click, 2840, true, false).Mouse.Count == 0, "clique mantido não repete");
processor.Process(touch, 2850, true, false);
var right = touch with { TouchpadPressed = true, Touch = new(new(1, 100, 100), new(2, 200, 100)) };
Check(processor.Process(right, 2860, true, false).Mouse.Single().Kind == "right-click", "pressão com dois dedos faz clique direito");
processor.Process(touch, 2870, true, false);
Check(processor.Process(click, 2880, false, false).Mouse.Count == 0 && !processor.Enabled, "fim da sessão bloqueia inclusive o último clique");
processor.Process(mic, 3000, true, false); processor.Process(mic, 4500, true, false);
Check(processor.Enabled, "mouse reativado"); processor.Process(null, 4510, true, true);
Check(!processor.Enabled && !processor.Released(4600), "desconexão/inválido limpa gesto, mouse e soltura");
processor.Process(mic, 4700, true, false);
Check(!processor.Enabled, "reconexão não reaproveita hold anterior");
processor.Reset(5000);
processor.Process(mic, 5100, false, false); processor.Process(neutral, 5180, false, false);
processor.Process(mic, 5300, false, false);
Check(processor.Process(neutral, 5380, false, false).ToggleLauncher, "duplo microfone abre painel fora de sessão");
processor.Reset(6000);
var chord = neutral with { LauncherChord = true, Neutral = false, Microphone = null, Touch = null };
processor.Process(chord, 6100, false, false);
Check(processor.Process(chord, 7100, false, false).ToggleLauncher, "Create+Options funciona sem microfone");

// A second adapter proves that policy does not require Sony bytes or features.
IControllerAdapter alternative = new ExamplePadAdapter();
processor.Reset(8000);
processor.Process(alternative.Decode([0]), 8000, true, true);
processor.Process(alternative.Decode([0]), 8080, true, true);
Check(processor.Process(alternative.Decode([1]), 8090, true, true).Navigation.Single().Key == 10009, "outro adaptador navega pelo mesmo contrato");
Console.WriteLine("PASS: adaptadores, USB/BT/básico, foco/soltura, microfone, mouse/cliques, sessão e reconexão sem efeitos no sistema.");

sealed class ExamplePadAdapter : IControllerAdapter {
    public string Id => "example-pad";
    public ControllerState? Decode(ReadOnlySpan<byte> report) => report.Length != 1 ? null :
        new(report[0] == 1 ? ControllerButtons.Back : ControllerButtons.None, report[0] == 0, null, null, false, false);
}
