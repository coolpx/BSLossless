# TODO

- ~~Playback at non-standard speeds sounds janky~~
- Play preview snippets from flac
- Adjust gain of flac to match original
- Flac overrides for OST tracks
- Mirror failure volume animation: `AudioPitchGainEffect` animates `AudioSource.volume` over 0.3s via `AnimationCurve` (likely linear 1→0). Game source is muted so can't read its volume during fail. Patch `AudioPitchGainEffect.StartEffect` to capture curve + notify controller, or detect fail state and animate sidecar volume ourselves.
