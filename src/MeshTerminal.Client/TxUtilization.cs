using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
namespace MeshTerminal.Client
{
    public sealed class TxUtilizationSnapshot
    {
        public double? Percent { get; internal set; }
        public double WindowSeconds { get; internal set; }
        public bool Estimated { get; internal set; }
        public DateTime ReceivedUtc { get; internal set; }
    }
    internal sealed class TxAirtimeWindow
    {
        private sealed class Sample { internal double Time;internal uint Counter; }
        private readonly object _gate=new object();
        private readonly LinkedList<Sample> _samples=new LinkedList<Sample>();
        private readonly Stopwatch _clock=Stopwatch.StartNew();
        internal void Reset() { lock(_gate) _samples.Clear(); }
        internal TxUtilizationSnapshot Observe(uint counter) { return Observe(counter,_clock.Elapsed.TotalSeconds,DateTime.UtcNow); }
        internal TxUtilizationSnapshot Observe(uint counter,double seconds,DateTime received)
        {
            lock(_gate)
            {
                var last=_samples.Last?.Value;
                if(last!=null && seconds<=last.Time) return null;
                // Counter resets/wraps and implausible jumps must not become a duty-cycle spike.
                if(last!=null && (counter<last.Counter || seconds-last.Time>120 || (double)counter-last.Counter>seconds-last.Time+2)) _samples.Clear();
                _samples.AddLast(new Sample { Time=seconds,Counter=counter });
                double cutoff=seconds-3600;
                while(_samples.Count>2 && _samples.First.Next.Value.Time<=cutoff) _samples.RemoveFirst();
                while(_samples.Count>2048) _samples.RemoveFirst();
                var first=_samples.First.Value;
                double baselineTime=first.Time,baselineCounter=first.Counter;
                if(first.Time<cutoff && _samples.First.Next!=null)
                {
                    var next=_samples.First.Next.Value;
                    double ratio=(cutoff-first.Time)/(next.Time-first.Time);
                    baselineTime=cutoff;baselineCounter=first.Counter+ratio*((double)next.Counter-first.Counter);
                }
                var window=seconds-baselineTime;
                return new TxUtilizationSnapshot { Percent=window<60?(double?)null:Math.Min(100,Math.Max(0,100*((double)counter-baselineCounter)/window)),WindowSeconds=window,Estimated=true,ReceivedUtc=received };
            }
        }
    }
    public static class TxUtilizationDisplay
    {
        public static string Window(double seconds)
        {
            if(seconds>=3600) return "1h";
            if(seconds>=60) return Math.Floor(seconds/60).ToString(CultureInfo.InvariantCulture)+"m";
            return Math.Max(0,Math.Floor(seconds)).ToString(CultureInfo.InvariantCulture)+"s";
        }
        public static string Compact(TxUtilizationSnapshot value,bool connected)
        {
            if(!connected||value==null) return "TX: -";
            if(!value.Percent.HasValue) return "TX: measuring";
            return "TX: "+(value.Estimated?"~":"")+value.Percent.Value.ToString("0.##",CultureInfo.InvariantCulture)+"%";
        }
        public static string Details(TxUtilizationSnapshot value,bool connected)
        {
            if(!connected||value==null) return "TX airtime utilization: Not available";
            var amount=value.Percent.HasValue?(value.Estimated?"approximately ":"")+value.Percent.Value.ToString("0.##",CultureInfo.InvariantCulture)+" %":"Measuring (minimum 60 seconds)";
            return "TX airtime utilization: "+amount+"\n"+(value.Estimated?"Observed window: ":"Device-reported window: ")+Window(value.WindowSeconds)+"\nTX statistics read (PC): "+value.ReceivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
    public sealed partial class MeshtasticClient
    {
        private readonly TxAirtimeWindow _txAirtimeWindow=new TxAirtimeWindow();
        private void ResetTxUtilization() { _txAirtimeWindow.Reset();Device.TxUtilization=null; }
        private void RecordReportedTxUtilization(float percent)
        {
            if(float.IsNaN(percent)||float.IsInfinity(percent)||percent<0||percent>100) return;
            Device.TxUtilization=new TxUtilizationSnapshot { Percent=percent,WindowSeconds=3600,Estimated=false,ReceivedUtc=DateTime.UtcNow };
        }
        private void RecordMeshCoreTxAirtime(uint seconds)
        {
            var sample=_txAirtimeWindow.Observe(seconds);
            if(sample!=null) Device.TxUtilization=sample;
        }
    }
}
