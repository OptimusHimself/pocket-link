using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

// All visual tokens and bundled artwork live here; resource workers do not depend on this class.
sealed class UiTheme : IDisposable {
    public readonly Color Background=Color.FromArgb(23,25,28),Text=Color.FromArgb(229,231,235),Muted=Color.FromArgb(160,164,171),Line=Color.FromArgb(45,48,53),Accent=Color.FromArgb(80,212,221),Error=Color.FromArgb(245,159,159);
    public readonly Color ControlBackground=Color.FromArgb(34,37,41),ControlHover=Color.FromArgb(55,59,65),ControlBorder=Color.FromArgb(74,78,85),SelectedBackground=Color.FromArgb(37,44,46);
    public readonly Font Body=new Font("Segoe UI",10),Small=new Font("Segoe UI",9),Title;
    public readonly Icon Icon;
    readonly PrivateFontCollection fonts=new PrivateFontCollection();IntPtr fontMemory;
    public UiTheme(){
        var assembly=typeof(UiTheme).Assembly;
        using(var stream=assembly.GetManifestResourceStream("PocketLink.Orbitron.ttf")){
            if(stream==null)throw new InvalidOperationException("Missing embedded Orbitron font.");
            byte[] bytes=new byte[stream.Length];stream.Read(bytes,0,bytes.Length);fontMemory=Marshal.AllocCoTaskMem(bytes.Length);Marshal.Copy(bytes,0,fontMemory,bytes.Length);fonts.AddMemoryFont(fontMemory,bytes.Length);
        }
        Title=new Font(fonts.Families[0],12,FontStyle.Regular);
        using(var stream=assembly.GetManifestResourceStream("PocketLink.ico"))using(var icon=new Icon(stream)){Icon=(Icon)icon.Clone();}
    }
    public void Dispose(){Title.Dispose();Body.Dispose();Small.Dispose();Icon.Dispose();fonts.Dispose();if(fontMemory!=IntPtr.Zero){Marshal.FreeCoTaskMem(fontMemory);fontMemory=IntPtr.Zero;}}
}
