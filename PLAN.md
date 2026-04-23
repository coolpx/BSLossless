# BSSidecarAudio

A Beat Saber mod that plays lossless audio from a song.flac alongside song.egg externally to the Beat Saber process and mutes built-in audio.

### 1. Architecture Overview
- **Detection**: On level load, check the custom level directory for `song.flac`.
- **Muting**: If present, find the level's `AudioSource` instances and set `.mute = true` / `.volume = 0`.
- **Playback**: Initialize BASS, load the FLAC stream, and sync its position to Beat Saber's internal clock rather than letting BASS play autonomously.
- **Sync Source**: Use `AudioTimeSyncController.songTime` and the `.egg` file's `songStart` offset. You don't need manual waveform alignment; the game already knows the correct audio start time.

### 2. Implementation Steps

**Phase 1: Project & Dependencies**
- Use the standard Beat Saber Mod Project template (V6).
- Add `Bass.Net` via NuGet or drop the managed BASS dlls into your mod folder. Register for a free BASS license key to unlock the .NET wrapper.
- Include `0Harmony` for patching game internals.

**Phase 2: File Detection & Audio Muting**
- Listen to `LevelLoader` or `GameplayCoreSceneSetupData` initialization via Harmony or IPA's `OnGameSceneActive` events.
- Resolve the level directory: `Directory.GetParent(beatmapLevel.levelInfo.songPath).FullName`
- Check for `song.flac`. If found, store the path and flag `useFlac = true`.
- Patch `AudioSource.Play` or iterate through `Object.FindObjectsOfType<AudioSource>()` tagged with `"Gameplay"` and mute them when `useFlac` is true.

**Phase 3: BASS Initialization & Stream Setup**
- In level setup, call `Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero)`
- Load the plugin: `Bass.BASS_PluginLoad("bassflac.dll")` (ship this alongside your mod)
- Create stream: `Bass.BASS_StreamCreateFile(flacPath, 0, 0, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_SAMPLE_FLOAT)`
- Get metadata: `Bass.BASS_ChannelGetInfo(stream, out info)` to sample rate and length in bytes.

**Phase 4: Synchronization (Critical)**
Beat Saber's `AudioTimeSyncController` drives all song timing. You must mirror it:
- In a `MonoBehaviour` with `LateUpdate` (or IPA's `OnUpdate`), read `AudioTimeSyncController.songTime`.
- Calculate target sample position:  
  `double targetSeconds = Math.Max(0, songTime - songStartOffset);`  
  `long targetByte = (long)(targetSeconds * info.frequency * info.channels * sizeof(float));`
- Sync BASS: `Bass.BASS_ChannelSetPosition(stream, targetByte, BASSMode.BASS_POS_BYTE)`
- Play/Resume: Only call `Bass.BASS_ChannelPlay(stream, false)` once. BASS will continue decoding, but you'll continuously correct its position each frame to prevent drift.
- Handle pauses, fails, and seeks by checking `AudioTimeSyncController.isPaused` and resetting position on `songTime` jumps.

**Phase 5: Edge Cases & Polish**
- `.egg` files may have different `songStart` values than the FLAC if the FLAC was exported without the original lead-in. Read `songStart` from the level's `BeatmapLevelData` (or parse the `.egg`'s JSON header) and apply it as the sync offset.
- Clamp position to stream length to avoid BASS errors on song end.
- Add a config toggle to revert to default audio if the FLAC fails to load.
- Ensure proper BASS cleanup on level unload: `Bass.BASS_ChannelFree(stream)`, `Bass.BASS_Free()`.

### 3. Known Pitfalls
- **Unity/BASS threading**: BASS decoding runs on a separate thread. Continuously setting position is safe, but avoid blocking calls.
- **Drift/latency**: If BASS falls behind, `BASS_ChannelSetPosition` will jump. Consider adding a small smoothing threshold (only seek if `|currentPos - targetPos| > X samples`).
- **BASS licensing**: The .NET wrapper requires a valid registration email embedded in your code to avoid watermark/debug messages.
- **Quest compatibility**: BASS requires native `.so` libraries for Linux/Android. This plan targets PC only; cross-platform would need separate native builds.

Start by implementing detection + muting, then verify that `AudioTimeSyncController.songTime` matches your expectations before wiring BASS position sync. Test with levels that have known `.egg` offsets first.