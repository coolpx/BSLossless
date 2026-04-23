# BSSidecarAudio

## Purpose
Beat Saber IPA plugin. Swap map audio with `song.flac` when file exists beside stock map audio. Keep Beat Saber timing source. Mute game `AudioSource`. Drive sidecar `AudioSource` from game clock.

## File Map
- `Plugin.cs`: IPA entry. Load config. Install Harmony patches. Spawn persistent controller.
- `HarmonyPatches.cs`: Detect `song.flac` during gameplay install. Cache paths + `songTimeOffset`. Forward Start/Pause/Resume/Seek hooks into controller.
- `BSSidecarAudioController.cs`: Runtime brain. Mute/unmute game source. Prepare sidecar playback. Apply latency + map offset + lead-in compensation. Resync in `LateUpdate`.
- `SidecarPlayback.cs`: Thin Unity `AudioSource` wrapper. Load FLAC via NAudio. Create/destroy `GameObject`. Clamp seeks. Track prepared/started state.
- `AudioAlignment.cs`: Compare Vorbis map audio vs FLAC envelope. Estimate FLAC lead-in trim compensation. Defensive fallback = `0f`.
- `Configuration/PluginConfig.cs`: `Enabled`, `SyncThreshold`.

## Runtime Flow
1. `GameplayCoreInstaller.InstallBindings` postfix finds map audio path.
2. If sibling `song.flac` exists, cache FLAC path + reference audio path + map `songTimeOffset`.
3. `AudioTimeSyncController.StartSong` postfix tells controller start sidecar flow.
4. Controller loads FLAC, estimates lead-in compensation, mutes stock audio, prepares Unity `AudioSource`.
5. `LateUpdate` waits for playable clock, starts sidecar once, then hard-resyncs when drift > `SyncThreshold`.
6. Pause/resume/seek hooks mirror game state. Scene unload or destroy restores stock audio and clears pending state.

## Timing Formula
Target sidecar time:

```csharp
songTime + songTimeOffset + audioLatency - clipLeadInCompensation
```

Meaning:
- `songTime`: Beat Saber transport clock.
- `songTimeOffset`: map metadata offset.
- `audioLatency`: private engine latency from `AudioTimeSyncController`.
- `clipLeadInCompensation`: measured silence or extra lead-in on FLAC front.

## Edit Notes
- Keep `AudioTimeSyncController` clock authoritative. Do not invent separate timer.
- Preserve mute restore path. Broken cleanup leaves game audio muted after scene exit.
- `StartFlacPlayback()` calls `_playback.Prepare()` first. `LateUpdate()` owns deferred start when song clock not ready yet.
- `HarmonyPatches.ClearPending()` part of cleanup contract. Needed between songs.
- `AudioAlignment` cost bounded on purpose: downsampled, envelope-only, first ~24s, max lag 8s.
- `SidecarPlayback.Time` clamps near clip end. Avoid exact clip length seek.

## Doc Style
Write terse. Explain why more than what. Good pattern:
- `Inline object allocates each frame. Cache or hoist.`
- `Use game clock here. Separate timer drifts on pause/seek.`
- `Clear pending FLAC on cleanup. Prevent stale path on next song.`

Avoid:
- Restating obvious syntax.
- Big theory dumps.
- Vague comments like `// handle audio`.

## Verify
- Build after code edits.
- Smoke test map with no `song.flac`: stock audio unchanged.
- Smoke test map with `song.flac`: stock audio muted, FLAC starts, pause/resume/seek stay aligned.
- Watch logs for `Envelope alignment` and cleanup path on scene unload.
