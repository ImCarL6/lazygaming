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
        Enabled = false; padPressed = false;
        shortcut.Reset(); doubleTap.Reset(); gesture.Reset(); launcherHold = new();
        neutral.Observe(false, now); ResetNavigation(now);
    }

    public ControllerOutput Process(ControllerState? state, long now, bool sessionActive, bool focusedPanel)
    {
        if (state == null) { Reset(now); return new([], [], false, null); }
        neutral.Observe(state.Neutral, now);
        bool active = menuGate.Update(focusedPanel, neutral.Ready(now));
        var keys = navigation.Update(state, now, active).Select(k => new NavigationInput(k, navigation.Repeated.Contains(k))).ToArray();
        bool launch = launcherHold.Update(state.LauncherChord, now);
        if (state.Microphone is bool mic) launch |= doubleTap.Update(mic, now);
        else { doubleTap.Reset(); shortcut.Reset(); }
        var mouse = new List<MouseAction>();
        bool? changed = null;
        // Session must be active before any click, including its final report.
        if (!sessionActive) { Enabled = false; shortcut.Reset(); gesture.Reset(); }
        else {
            if (Enabled && state.TouchpadPressed && !padPressed)
                mouse.Add(new(state.Touch is { First: not null, Second: not null } ? "right-click" : "left-click"));
            if (state.Microphone is bool pressed && shortcut.Update(pressed, now)) {
                Enabled = !Enabled; changed = Enabled; gesture.Reset();
            }
            if (state.Touch == null || !Enabled || shortcut.Holding) gesture.Reset();
            else mouse.AddRange(gesture.Update(state.Touch, now));
        }
        padPressed = state.TouchpadPressed;
        return new(keys, mouse, launch, changed);
    }
}
