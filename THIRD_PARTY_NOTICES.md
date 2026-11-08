# Third-party components

PocketLink's original code is MIT licensed. Third-party code, fonts and tools retain their own licenses; the root MIT license does not replace them.

| Component | Use | License / source |
| --- | --- | --- |
| AudioShare | Android playback server and native audio capture | LGPL-3.0-or-later; [upstream v2.2.2](https://github.com/ysbing/AudioShare/tree/v2.2.2), see `third_party/AudioShare/LICENSE` and `NOTICE.md` |
| Orbitron | Embedded UI font | SIL Open Font License 1.1; see `assets/fonts/Orbitron-OFL.txt` |
| scrcpy | Android camera and microphone transport | Apache-2.0; [upstream](https://github.com/Genymobile/scrcpy) |
| Android platform-tools | USB debugging transport | See the official platform-tools distribution's notices; [download](https://developer.android.com/tools/releases/platform-tools) |
| FFmpeg | Video decoding and JPEG conversion | LGPL/GPL depending on the selected build; [licenses](https://ffmpeg.org/legal.html) |
| OBS Studio | Virtual camera output | GPL-2.0-or-later; [upstream](https://github.com/obsproject/obs-studio) |
| VB-CABLE | Optional virtual microphone route | Proprietary donationware; [vendor](https://vb-audio.com/Cable/) |

Downloaded executables, installers, drivers, local media and runtime logs are excluded from Git. Using a separate component does not relicense it under MIT. A runtime bundle produced by `Publish-PocketLink.ps1` includes downloaded components; redistribution of such bundles must retain the applicable component licenses and notices.
