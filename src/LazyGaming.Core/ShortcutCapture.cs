namespace LazyGaming;

public sealed record ShortcutCaptureState(string Phase, ShortcutBinding? Binding, bool SinglePress = false);

// Explicit, short-lived recording only. No game input interception or remapping.
public sealed class ShortcutCapture
{
    readonly object gate = new();
    string phase = "idle";
    long started, pressedAt, releasedAt, neutralAt = -1;
    PadButtons held, firstTap;
    ShortcutBinding? result;
    bool singlePress;
    void Expire(long now) {
        if(phase=="listening" && held==PadButtons.None && firstTap!=PadButtons.None && now-releasedAt>400) {
            result=new(firstTap);singlePress=true;phase="done";
        }
        if (phase is "release" or "listening" && now-started>=15000) phase="timeout";
    }
    public ShortcutCaptureState State(long now) { lock(gate){Expire(now);return new(phase,result,singlePress);} }
    public bool Active(long now) => State(now).Phase is "release" or "listening";
    public void Begin(long now) { lock(gate) {
        Expire(now); if(phase is "release" or "listening")throw new InvalidOperationException("Uma gravação já está em andamento.");
        started=now;neutralAt=-1;held=firstTap=PadButtons.None;result=null;singlePress=false;phase="release";
    } }
    public void Cancel() { lock(gate){phase="idle";held=firstTap=PadButtons.None;result=null;} }
    public bool Observe(ControllerState? state,long now) { lock(gate) {
        Expire(now);if(phase is not ("release" or "listening"))return false;
        if(state?.RawButtons is not PadButtons raw){neutralAt=-1;held=firstTap=PadButtons.None;phase="release";return true;}
        if(phase=="release") {
            if(!state.Neutral){neutralAt=-1;return true;}
            if(neutralAt<0)neutralAt=now;
            if(now-neutralAt>=80)phase="listening";
            return true;
        }
        if(raw!=PadButtons.None) {
            if(held==PadButtons.None){pressedAt=now;held=raw;}
            else held|=raw;
        } else if(held!=PadButtons.None) {
            long duration=now-pressedAt;
            if(duration>=500) result=new(held,"hold",(int)Math.Clamp(Math.Round(duration/500d)*500,500,3000));
            else if(duration<=300 && held==firstTap && now-releasedAt<=400)result=new(held,"double-tap");
            else {firstTap=duration<=300?held:PadButtons.None;releasedAt=now;}
            held=PadButtons.None;
            if(result!=null)phase="done";
        }
        return true; // Includes completion report; it must not trigger the old shortcut.
    } }
}
