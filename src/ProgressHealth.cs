using System;
using System.Diagnostics;

sealed class ProgressHealth {
    readonly Func<long> clock;readonly long frequency;long last;
    public ProgressHealth():this(Stopwatch.GetTimestamp,Stopwatch.Frequency){}
    internal ProgressHealth(Func<long> clock,long frequency){this.clock=clock;this.frequency=frequency;Pulse();}
    public void Pulse(){System.Threading.Interlocked.Exchange(ref last,clock());}
    public bool Expired(int milliseconds){return (clock()-System.Threading.Interlocked.Read(ref last))/(double)frequency*1000>=milliseconds;}
}
