using LazyGaming;

static class NoticePolicyTests
{
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    public static void Run() {
        var notices=new NoticePolicy();
        Check(notices.Accept("missing dependency",0),"first actionable notice allowed");
        for(int t=10;t<5000;t+=10)Check(!notices.Accept("missing dependency",t),"repeating loop never restarts notice");
        Check(!notices.Accept("another error",1000),"different errors also have a global interval");
        Check(notices.Accept("another error",6000),"later independent problem can be shown");
        Check(!notices.Accept("missing dependency",29999) && notices.Accept("missing dependency",30000),"same issue can reappear after cooldown");
        Check(NoticePolicy.ReadingDuration("Short") == 3500 && NoticePolicy.ReadingDuration(new string('x',20000)) == 6500,"reading duration remains bounded");
        var modes=new NoticePolicy();
        Check(modes.Accept("on",0,800,0) && !modes.Accept("on",100,800,0) && modes.Accept("off",110,800,0),"duplicates suppressed without hiding real mode change");
        Console.WriteLine("PASS: notification bursts/cooldowns, distinct errors, readable bounded durations and explicit mode changes.");
    }
}
