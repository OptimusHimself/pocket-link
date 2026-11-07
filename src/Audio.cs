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

static class Pcm {
    public static void ReadExact(Stream stream, byte[] data) {
        int pos=0; while(pos<data.Length) { int n=stream.Read(data,pos,data.Length-pos); if(n==0) throw new EndOfStreamException("Audio connection closed"); pos+=n; }
    }
}
