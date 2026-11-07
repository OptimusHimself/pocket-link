using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Native {
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_Initialize();
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_Listen(int port, IntPtr callback);
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_IsConnected(IntPtr text, int size);
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_Start();
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void AudioCapture_Cleanup();
}

// One input frame produces one sample per phone. Delay never changes sample count.
public sealed class ChannelDelay {
    readonly byte[] ring; int pos;
    public ChannelDelay(int samples) { ring = new byte[samples * 2]; }
    public void Apply(byte[] data) {
        if (ring.Length == 0) return;
        for (int i=0;i<data.Length;i++) { byte old=ring[pos]; ring[pos]=data[i]; data[i]=old; pos=(pos+1)%ring.Length; }
    }
}
static class Pcm {
    public static void Split(byte[] stereo, int count, byte[] left, byte[] right) {
        if (count%4!=0) throw new ArgumentException("Incomplete stereo frame");
        for(int i=0,j=0;i<count;i+=4,j+=2) { left[j]=stereo[i]; left[j+1]=stereo[i+1]; right[j]=stereo[i+2]; right[j+1]=stereo[i+3]; }
    }
    public static byte[] Header(int rate) {
        byte[] h=new byte[8]; Array.Copy(BitConverter.GetBytes(rate),h,4); h[4]=1; h[6]=16; return h;
    }
    public static void ReadExact(Stream stream, byte[] data) {
        int pos=0; while(pos<data.Length) { int n=stream.Read(data,pos,data.Length-pos); if(n==0) throw new EndOfStreamException("Audio connection closed"); pos+=n; }
    }
}


