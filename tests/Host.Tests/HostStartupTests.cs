using LazyGaming;

static class HostStartupTests
{
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    public static void Run() {
        Check(HostStartup.CanStart(47831,()=>false,()=>false),"fresh startup skips an unreliable HTTP probe only with proven absence");
        Check(!HostStartup.CanStart(47831,()=>true,()=>false),"occupied listener never permits another helper");
        Check(!HostStartup.CanStart(47831,()=>false,()=>true),"helper still starting never permits duplicate");
        Check(!HostStartup.CanStart(47831,()=>throw new InvalidOperationException(),()=>false),"unknown local state remains conservative");
        var directory=Path.Combine(Path.GetTempPath(),"lg-instance-"+Guid.NewGuid().ToString("N"));
        try {
            using(var first=HostInstanceLease.Acquire(directory)) {
                bool refused=false;
                try { using var second=HostInstanceLease.Acquire(directory); }catch(InvalidOperationException){refused=true;}
                Check(refused,"one data profile can only have one owner");
            }
            using var after=HostInstanceLease.Acquire(directory);
        } finally { Directory.Delete(directory,true); }
        Console.WriteLine("PASS: fresh startup, live/pending/unknown helper protection and exclusive profile lease without HTTP/TV/game actions.");
    }
}
