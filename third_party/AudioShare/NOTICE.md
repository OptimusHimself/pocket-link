# AudioShare components

Upstream: https://github.com/ysbing/AudioShare/tree/v2.2.2
License: LGPL-3.0-or-later (see LICENSE).

SoundLink loads the unmodified `audio_capture.dll` and Android `server` from the
upstream Windows 2.2.2 release. They remain separate, replaceable components.
The capture source and Android entry point are included here for inspection;
the complete corresponding project and build instructions are available at
the tagged upstream link above. No upstream binaries are committed to Git.

The project uses Google's portable Android platform-tools (37.0.1) for ADB.
Downloads stay under tools/ and are excluded from version control.
