namespace LazyGaming;

public sealed record NavigationInput(int Key, bool Repeat);
public sealed record ControllerOutput(IReadOnlyList<NavigationInput> Navigation,
    IReadOnlyList<MouseAction> Mouse, bool ToggleLauncher, bool? MouseModeChanged);

// All time and platform state are supplied by the host; this class has no OS effects.
public sealed class ControllerProcessor
{
    readonly TouchpadGesture gesture = new();
    readonly HoldToggle shortcut = new();
    readonly DoubleTap doubleTap = new();
    LauncherHold launcherHold = new();
    readonly NativeNavigation navigation = new();
    readonly ControllerNeutralState neutral = new();
    readonly MenuNavigationGate menuGate = new();
    bool padPressed;
    // Touchpad pressionado com um dedo segura o botão esquerdo (arrastar janela, barra de rolagem, seleção).
    bool dragging, dragMoving; int dragSlopX, dragSlopY;
    public bool Dragging => dragging;
    const int DragSlop = 24;
    readonly ConfiguredShortcut configuredLauncher = new(), configuredMouse = new();
    readonly StickPointer pointer = new();
    PadButtons previousButtons;
    bool clicksReady;
    ControllerOptions? lastOptions;
    public bool Enabled { get; private set; }
    public bool NavigationArmed => menuGate.Armed;
    public bool Released(long now) => neutral.Ready(now);
    public void ResetNavigation(long now)
    {
        menuGate.Update(false, false);
        navigation.Update((ControllerState?)null, now, false);
    }

    public void Reset(long now)
    {
        Enabled = false; padPressed = false; dragging = dragMoving = false; dragSlopX = dragSlopY = 0;
        shortcut.Reset(); doubleTap.Reset(); gesture.Reset(); launcherHold = new();
        configuredLauncher.Reset(); configuredMouse.Reset(); pointer.Reset(); clicksReady = false; previousButtons = PadButtons.None;
        neutral.Observe(false, now); ResetNavigation(now);
    }

    public ControllerOutput Process(ControllerState? state, long now, bool sessionActive, bool focusedPanel, ControllerOptions? options = null)
    {
        options ??= new();
        // Troca de opções (ex.: Bluetooth básico ↔ completo) reinicia o estado sem deixar o botão esquerdo preso.
        bool releaseDrag = false;
        if (lastOptions != options) { releaseDrag = dragging; Reset(now); lastOptions = options; }
        if (state == null) { bool release = dragging || releaseDrag; Reset(now); return new([], release ? [new MouseAction("left-up")] : [], false, null); }
        neutral.Observe(state.Neutral, now);
        bool active = menuGate.Update(focusedPanel, neutral.Ready(now));
        var buttons = state.Buttons;
        if (options.SwapConfirmBack) {
            buttons &= ~(ControllerButtons.Confirm | ControllerButtons.Back);
            if (state.Buttons.HasFlag(ControllerButtons.Confirm)) buttons |= ControllerButtons.Back;
            if (state.Buttons.HasFlag(ControllerButtons.Back)) buttons |= ControllerButtons.Confirm;
        }
        if (state.RawButtons.HasValue) {
            var reserved = options.LauncherBinding.Buttons | options.MouseBinding.Buttons;
            if (Enabled) reserved |= options.LeftClick | options.RightClick;
            if ((reserved & PadButtons.South) != 0) buttons &= ~(options.SwapConfirmBack ? ControllerButtons.Back : ControllerButtons.Confirm);
            if ((reserved & PadButtons.East) != 0) buttons &= ~(options.SwapConfirmBack ? ControllerButtons.Confirm : ControllerButtons.Back);
            // Volume rápido só quando o botão superior não pertence a um atalho configurado.
            if ((state.RawButtons.Value & PadButtons.North) != 0 && (reserved & PadButtons.North) == 0) buttons |= ControllerButtons.Volume;
        }
        var keys = navigation.Update(state with { Buttons = buttons }, now, active).Select(k => new NavigationInput(k, navigation.Repeated.Contains(k))).ToArray();
        bool launch = state.RawButtons is PadButtons raw ? configuredLauncher.Update(raw, options.LauncherBinding, now) : launcherHold.Update(state.LauncherChord, now);
        bool legacyMic = options.LegacyMicrophone && ((options.LauncherBinding.Buttons | options.MouseBinding.Buttons) & PadButtons.Microphone) == 0;
        if (legacyMic && state.Microphone is bool mic) launch |= doubleTap.Update(mic, now);
        else { doubleTap.Reset(); shortcut.Reset(); }
        var mouse = new List<MouseAction>();
        if (releaseDrag) mouse.Add(new("left-up"));
        bool? changed = null;
        // Session must be active before any click, including its final report.
        bool mouseToggle = state.RawButtons is PadButtons input && configuredMouse.Update(input, options.MouseBinding, now);
        if (!sessionActive) { if (dragging) mouse.Add(new("left-up")); dragging = false; Enabled = false; shortcut.Reset(); gesture.Reset(); pointer.Reset(); clicksReady = false; }
        else {
            if (Enabled && state.TouchpadPressed && !padPressed) {
                if (state.Touch is { First: not null, Second: not null }) mouse.Add(new("right-click"));
                else { mouse.Add(new("left-down")); dragging = true; dragMoving = false; dragSlopX = dragSlopY = 0; }
            }
            if (dragging && !state.TouchpadPressed) { mouse.Add(new("left-up")); dragging = false; }
            if (mouseToggle || (legacyMic && state.Microphone is bool pressed && shortcut.Update(pressed, now))) {
                if (dragging) { mouse.Add(new("left-up")); dragging = false; }
                Enabled = !Enabled; changed = Enabled; gesture.Reset(); pointer.Reset(); clicksReady = false;
            }
            if (state.Touch == null || !Enabled || shortcut.Holding || configuredMouse.Holding) gesture.Reset();
            else foreach (var action in gesture.Update(state.Touch, now)) {
                // Pressionar o touchpad desloca o dedo alguns pontos: só arrastar depois da zona morta.
                if (dragging && !dragMoving && action.Kind == "move") {
                    dragSlopX += action.X; dragSlopY += action.Y;
                    if (Math.Abs(dragSlopX) + Math.Abs(dragSlopY) < DragSlop) continue;
                    dragMoving = true; mouse.Add(new("move", dragSlopX, dragSlopY)); continue;
                }
                mouse.Add(action);
            }
            if (Enabled && state.RawButtons is PadButtons held && !mouseToggle) {
                if (held == PadButtons.None) clicksReady = true;
                if (clicksReady) {
                    if ((held & options.LeftClick) == options.LeftClick && (previousButtons & options.LeftClick) != options.LeftClick) mouse.Add(new("left-click"));
                    if ((held & options.RightClick) == options.RightClick && (previousButtons & options.RightClick) != options.RightClick) mouse.Add(new("right-click"));
                }
                if (pointer.Update(state.RightX, state.RightY, now, options) is { } movement) mouse.Add(movement);
            } else pointer.Reset();
        }
        previousButtons = state.RawButtons ?? PadButtons.None;
        padPressed = state.TouchpadPressed;
        return new(keys, mouse, launch, changed);
    }
}
