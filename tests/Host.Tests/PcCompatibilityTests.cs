using LazyGaming;
using System.Text.Json;

// Files and fake adapters only. Never call native display/audio/driver APIs.
static class PcCompatibilityTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static string Snapshot(Displays.Snapshot value) => JsonSerializer.Serialize(value,new JsonSerializerOptions { IncludeFields=true });
    static Displays.Snapshot Screen(uint adapter, uint target, uint width, uint height, int x=0, uint rate=60, uint denominator=1) => new(
        [new() { Flags=1, Source=new() { Adapter=new() {Low=adapter,High=7},Id=0,ModeIndex=0 },
            Target=new() { Adapter=new() {Low=adapter,High=7},Id=target,ModeIndex=1,Available=true,Rotation=1,Scaling=1,Refresh=new(){Numerator=rate,Denominator=denominator} } }],
        [new() {Type=1,Adapter=new(){Low=adapter,High=7},Id=0,Source=new(){Width=width,Height=height,PixelFormat=4,Position=new(){X=x}}},
         new() {Type=2,Adapter=new(){Low=adapter,High=7},Id=target,Target=new(){Signal=new(){ActiveSize=new(){Width=width,Height=height},VSync=new(){Numerator=rate,Denominator=denominator}}}}]);
    public static async Task Run() {
        var root=Path.Combine(Path.GetTempPath(),"lg-compatibility-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalPreferences=Displays.Preferences;
        try {
            // Explicit endpoint selection uses IDs, not AMD/NVIDIA/Intel branding.
            var native=new NativeSessionPlatform();
            foreach (var vendor in new[]{"AMD High Definition Audio Device","NVIDIA High Definition Audio","Intel Display Audio"}) {
                var id="endpoint-"+vendor;
                var devices=new List<AudioDevice>{new(id,"LG TV ("+vendor+")",false),new("speaker","Speakers",true)};
                Displays.Preferences=()=>new(AudioDeviceId:id);
                Check(native.SessionAudio(devices)?.Id==id,"endpoint independent of GPU vendor");
                Check(native.SessionAudio(devices.Where(d=>d.Id!=id).ToList())==null,"missing endpoint never selects another output");
            }
            Displays.Preferences=()=>new(AudioDeviceId:"keep");
            Check(native.KeepAudio,"keep current audio does not depend on GPU brand");
            Displays.Preferences=originalPreferences;

            // Same target/source numbers on two adapters are different devices.
            var tv=Screen(2,1,3840,2160,rate:60000,denominator:1001);
            var desktop=Screen(1,1,2560,1440,-2560,144);
            var before=Snapshot(desktop);
            var available=new Displays.Snapshot([desktop.Paths[0],tv.Paths[0]],[]);
            var planned=Displays.PlanMonitors(tv,desktop,available,tv.Paths[0],true);
            Check(planned.Paths.Length==2 && planned.Paths[0].Source.Adapter.Low==2 && planned.Paths[1].Source.Adapter.Low==1,"hybrid adapters preserve physical sources");
            Check(planned.Paths[0].Target.Refresh.Numerator==60000 && planned.Paths[0].Target.Refresh.Denominator==1001 && planned.Paths[1].Target.Refresh.Numerator==144,"59.94 and 144 Hz remain exact");
            Check(before==Snapshot(desktop),"planning preserves original recovery snapshot");
            var replacement=Screen(9,1,2560,1440,-2560,144);
            bool refused=false;
            try { Displays.PlanMonitors(tv,desktop,new([replacement.Paths[0],tv.Paths[0]],[]),tv.Paths[0],true); }
            catch(InvalidOperationException){refused=true;}
            Check(refused && before==Snapshot(desktop),"changed driver adapter identity refuses to guess another monitor");
            int calls=0;
            Check(Displays.WaitForStableLayout(()=>{if(++calls<5)throw new InvalidOperationException("video temporarily unavailable");return true;},()=>{}),"transient query failures can stabilize");
            calls=0;
            Check(!Displays.WaitForStableLayout(()=>{calls++;return false;},()=>{}) && calls==24,"unstable video has bounded confirmation attempts");

            var sessionRoot=Path.Combine(root,"session"); Directory.CreateDirectory(sessionRoot);
            var output=new FaultOutput(desktop);
            var session=new TvSession(sessionRoot,output);session.SetMonitors(false);session.Prepare();
            var journal=Path.Combine(sessionRoot,"session.json");var journalBefore=File.ReadAllText(journal);
            output.QueryFailure=true;output.AudioFailure=true;
            for(int i=0;i<5;i++)session.Tick();
            Check(!session.DisplayControls.Available && File.ReadAllText(journal)==journalBefore && output.Preparations==1,"video/audio loss never overwrites snapshot or repeatedly changes displays");
            refused=false;try{session.SetMonitors(true);}catch(InvalidOperationException){refused=true;}
            Check(refused && output.MonitorChanges==1,"monitor change refused while driver state is unknown");
            output.QueryFailure=false;output.AudioFailure=false;output.Devices=[new("tv-amd","TV (AMD)",false),new("speaker","Speakers",true)];
            session.Tick();Check(output.Selected.SequenceEqual(["tv-amd"]),"late HDMI audio can reconnect");
            session.ManualAudio("speaker");session.Tick();
            Check(output.Selected.SequenceEqual(["tv-amd","speaker"]),"manual audio survives automatic retry");
            output.RestoreFailure=true;session.Restore();
            Check(session.Active && File.Exists(journal),"restore failure retains recovery journal");
            var restoreAttempts=output.Restorations;session.Tick();session.Restore();
            Check(output.Restorations==restoreAttempts,"failed restore does not loop while driver is unavailable");
            var recoveredOutput=new FaultOutput(desktop);
            var restarted=new TvSession(sessionRoot,recoveredOutput);restarted.Restore();
            Check(!restarted.Active && !File.Exists(journal) && recoveredOutput.AudioRestored && recoveredOutput.Restored==before,"restart can restore exact original displays/audio after simulated driver recovery");

            var failureRoot=Path.Combine(root,"prepare-failure");Directory.CreateDirectory(failureRoot);
            var failedOutput=new FaultOutput(desktop){PrepareFailure=true};var failedSession=new TvSession(failureRoot,failedOutput);failedSession.SetMonitors(false);
            refused=false;try{failedSession.Prepare();}catch(InvalidOperationException){refused=true;}
            Check(refused && !failedSession.Active && failedOutput.Restored==before,"preparation failure rolls back instead of launching with an unconfirmed display");

            // OneDrive-like name only: no access to real cloud/user Desktop folders.
            var profile=Path.Combine(root,"OneDrive - Equipe João & Filhos","Área de Trabalho","LazyGaming 'amigos' [1]");
            var installed=Path.Combine(profile,"Jogos","Aventura ação (64 bits)");Directory.CreateDirectory(installed);
            var exe=Path.Combine(installed,"Aventura.exe");File.WriteAllText(exe,"fixture; never executed");
            var data=Path.Combine(root,"LocalAppData","LazyGaming");Directory.CreateDirectory(data);
            var settings=new LibrarySettings(data);settings.Save(new([new(Path.Combine(profile,"Jogos"))],Steam:false,Automatic:false));
            var metadata=new GameMetadata(null,"Aventura",2026,[],[],"",DateTime.UtcNow,Source:"local",Artwork:new("","","",""));
            var manifest=Path.Combine(root,"ficha ação.json");File.WriteAllText(manifest,JsonSerializer.Serialize(new LocalGameRegistration(exe,installed,metadata)));
            new LocalGameStore(data).Import(manifest);
            var library=new GameLibrary(data,steamPath:Path.Combine(root,"no-steam"));
            Check(library.Games.Count==1,"accented OneDrive-style folder deduplicates manual and automatic registrations");
            var launch=WindowsGames.LaunchInfo(library.Games.Single());
            Check(launch.FileName==exe && launch.WorkingDirectory==installed && !launch.UseShellExecute && launch.ArgumentList.Count==0,"spaces, ampersands and quotes remain literal executable paths");
            var preferences=new PcPreferenceStore(data);var saved=new PcPreferences(ControllerSource:"xinput",SetupCompleted:true);preferences.Save(saved);
            using(var locked=new FileStream(Path.Combine(data,"pc-settings.json"),FileMode.Open,FileAccess.Read,FileShare.None)) {
                refused=false;try{preferences.Save(saved with{Enabled=false});}catch(Exception e) when(e is IOException or UnauthorizedAccessException){refused=true;}
                Check(refused && preferences.Current==saved,"file lock reports failure without changing confirmed in-memory preferences");
            }
            Check(new PcPreferenceStore(data).Current==saved,"complete primary settings survive temporary file lock");
            File.Move(exe,exe+".offline");library.Refresh();
            var inspected=JsonSerializer.Serialize(new LocalGameStore(data).Inspect());
            Check(library.Games.Count==0 && inspected.Contains("\"available\":false") && inspected.Contains("Aventura"),"unavailable synced game remains registered and is not executed");
            await Task.CompletedTask;
            Console.WriteLine("PASS: compatibility simulations: AMD/NVIDIA/Intel endpoints, hybrid adapters/59.94 Hz, driver-loss recovery journal, Unicode/cloud-style paths, locked settings and missing games; no hardware changes.");
        } finally { Displays.Preferences=originalPreferences;Directory.Delete(root,true); }
    }
    sealed class FaultOutput(Displays.Snapshot original) : ISessionPlatform {
        public bool QueryFailure,AudioFailure,RestoreFailure,PrepareFailure,Prepared,AudioRestored;
        public int Preparations,MonitorChanges,Restorations;
        public string? Restored;
        public List<string> Selected=[];
        public List<AudioDevice> Devices=[];
        public void ValidateTv() { }
        public Displays.Snapshot CaptureDisplays()=>original;
        public void PrepareTv(){Preparations++;if(PrepareFailure)throw new InvalidOperationException("driver temporarily unavailable");Prepared=true;}
        public bool TvReady(){if(QueryFailure)throw new InvalidOperationException("query unavailable");return Prepared;}
        public void ConfigureMonitors(Displays.Snapshot s,bool enabled){MonitorChanges++;}
        public void RestoreDisplays(Displays.Snapshot s){Restorations++;if(RestoreFailure)throw new InvalidOperationException("driver unavailable");Restored=Snapshot(s);Prepared=false;}
        public List<AudioDevice> ListAudio(){if(AudioFailure)throw new InvalidOperationException("audio reconnecting");return Devices;}
        public AudioDevice? SessionAudio(List<AudioDevice> devices)=>devices.SingleOrDefault(d=>d.Id=="tv-amd");
        public string[] CaptureAudio()=>["original-console","original-media","original-communications"];
        public void SelectAudio(string id){Selected.Add(id);Devices=Devices.Select(d=>d with{Current=d.Id==id}).ToList();}
        public void RestoreAudio(string[] ids){Check(ids.SequenceEqual(CaptureAudio()),"all audio roles preserved");AudioRestored=true;}
    }
}
