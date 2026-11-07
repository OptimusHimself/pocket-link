static class DevicePresence {
    public static bool MustRelease(bool previouslyReady,bool currentlyReady,bool resourcesBusy){return !currentlyReady&&(previouslyReady||resourcesBusy);}
    public static bool CanResumeSpeaker(string expected,string connected,bool ready,bool optedIn){return optedIn&&ready&&expected!=null&&expected==connected;}
}
