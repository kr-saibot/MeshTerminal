using System;
using System.Collections.Concurrent;
namespace MeshTerminal.Client
{
    public sealed class RepeaterMonitorSettings
    {
        public string DevicePublicKey { get; set; }
        public string PublicKey { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public int IntervalMinutes { get; set; }=15;
        public bool IncludeNeighborCount { get; set; }=true;
    }
    public sealed class RepeaterMonitorSchedule
    {
        private readonly ConcurrentDictionary<string,DateTime> _due=new ConcurrentDictionary<string,DateTime>();
        public bool TryBegin(RepeaterMonitorSettings job,string device,DateTime now)
        {
            if(job==null||!job.Enabled||String.IsNullOrWhiteSpace(device)||!String.Equals(job.DevicePublicKey,device,StringComparison.OrdinalIgnoreCase)||String.IsNullOrWhiteSpace(job.PublicKey)) return false;
            var key=device.ToLowerInvariant()+":"+job.PublicKey.ToLowerInvariant();
            DateTime next;
            if(_due.TryGetValue(key,out next) && now<next) return false;
            _due[key]=now.AddMinutes(Math.Max(1,Math.Min(10080,job.IntervalMinutes)));
            return true;
        }
        public void Complete(RepeaterMonitorSettings job,DateTime now)
        { _due[(job.DevicePublicKey??"").ToLowerInvariant()+":"+(job.PublicKey??"").ToLowerInvariant()]=now.AddMinutes(Math.Max(1,Math.Min(10080,job.IntervalMinutes))); }
        public void Reset(RepeaterMonitorSettings job) { DateTime ignored;_due.TryRemove((job.DevicePublicKey??"").ToLowerInvariant()+":"+(job.PublicKey??"").ToLowerInvariant(),out ignored); }
    }
}
