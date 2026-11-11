using System;
using System.IO;
using System.Linq;
using MeshTerminal.Client;
internal static class RepeaterMonitoringTests
{
    static void Check(bool condition,string message) { if(!condition) throw new Exception("Repeater monitoring: "+message); }
    internal static void Run()
    {
        var schedule=new RepeaterMonitorSchedule();var now=DateTime.UtcNow;
        var job=new RepeaterMonitorSettings { DevicePublicKey="self",PublicKey="peer" };
        Check(!job.Enabled&&job.IntervalMinutes==15&&job.IncludeNeighborCount,"Safe opt-in defaults and 15-minute interval");
        Check(!schedule.TryBegin(job,"self",now),"Disabled job never polls");job.Enabled=true;
        Check(!schedule.TryBegin(job,"other",now),"A different local radio never runs this job");
        Check(schedule.TryBegin(job,"self",now)&&!schedule.TryBegin(job,"self",now.AddMinutes(14)),"First run and interval gating");
        schedule.Complete(job,now.AddMinutes(2));
        Check(!schedule.TryBegin(job,"self",now.AddMinutes(16))&&schedule.TryBegin(job,"self",now.AddMinutes(17)),"Slow jobs wait a full interval after completion instead of catching up");
        schedule.Reset(job);Check(schedule.TryBegin(job,"self",now),"Saving settings makes the job eligible again");
        var path=Path.Combine(Path.GetTempPath(),"repeater-monitor-"+Guid.NewGuid().ToString("N")+".xml");
        try {
            var settings=new MeshtasticApplicationSettings();settings.RepeaterMonitors.Add(job);MeshtasticSettingsStore.Save(path,settings);
            var loaded=MeshtasticSettingsStore.Load(path).RepeaterMonitors.Single();
            Check(loaded.DevicePublicKey=="self"&&loaded.PublicKey=="peer"&&loaded.Enabled&&loaded.IncludeNeighborCount&&loaded.IntervalMinutes==15,"Per-device monitoring configuration survives restart");
        }
        finally { if(File.Exists(path)) File.Delete(path); }
        Console.WriteLine("Repeater monitoring tests passed: opt-in, own-device isolation, cadence after slow polls and restart persistence.");
    }
}
