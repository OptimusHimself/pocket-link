using System.Collections.Generic;

// Selection policy belongs to the application layer, independent of rendering.
static class UiSelection {
    public static Device PreferredPhone(List<Device> devices,string previous){return devices.Find(d=>d.Ready&&d.Serial==previous)??devices.Find(d=>d.Ready);}
    public static WaveDevice UniqueMicrophone(List<WaveDevice> devices){return devices.Count==1?devices[0]:null;}
}
