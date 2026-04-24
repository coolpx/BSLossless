using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace BSSidecarAudio
{
    public class BSSidecarAudioController : MonoBehaviour
    {
        public static BSSidecarAudioController Instance { get; private set; }

        private SidecarPlayback _playback;
        private AudioTimeSyncController _syncController;
        private AudioSource _mutedGameSource;
        private AudioMixerGroup _gameMixerGroup;
        private float _originalVolume;
        private bool _originalMute;
        private bool _overrideActive;
        private float _songTimeOffset;
        private float _audioLatency;
        private float _clipLeadInCompensation;
        private bool _playbackStarted;
        private float _lastPitch = 1f;
        private AnimationCurve _failGainCurve;
        private float _failDuration;
        private float _failStartTime;
        private bool _failAnimating;

        private void Awake()
        {
            if (Instance != null)
            {
                Plugin.Log.Warn($"Instance of {GetType().Name} already exists, destroying.");
                DestroyImmediate(this);
                return;
            }
            DontDestroyOnLoad(this);
            Instance = this;
            Plugin.Log.Debug($"{name}: Awake()");
        }

        private void OnEnable()
        {
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnDestroy()
        {
            Cleanup();
            HarmonyPatches.ClearPreviewCache();
            if (Instance == this) Instance = null;
            Plugin.Log.Debug($"{name}: OnDestroy()");
        }

        private void LateUpdate()
        {
            if (!_overrideActive || _playback == null || _syncController == null)
                return;

            _playback.TickFade(Time.deltaTime);

            if (!_playbackStarted)
            {
                if (!CanStartPreparedPlayback())
                    return;

                _playback.Time = GetTargetPlaybackTime();
                _playback.Start();
                _playbackStarted = true;
            }

            float pitch = _mutedGameSource != null
                ? _mutedGameSource.pitch : 1f;
            _playback.Speed = pitch;

            if (!_playback.SeekPending)
            {
                float pitchDelta = Mathf.Abs(pitch - _lastPitch);
                _lastPitch = pitch;

                if (Mathf.Approximately(pitchDelta, 0f))
                {
                    float targetTime = GetTargetPlaybackTime();
                    float currentTime = _playback.Time;
                    float diff = targetTime - currentTime;
                    float threshold =
                        Configuration.PluginConfig.Instance?.SyncThreshold
                        ?? 0.05f;

                    if (Mathf.Abs(diff) > threshold)
                        _playback.QueueSeek(targetTime);
                }
            }

            if (_failAnimating && _playback != null)
            {
                float elapsed = Time.time - _failStartTime;
                if (elapsed >= _failDuration)
                {
                    _playback.VolumeScale = 0f;
                    _failAnimating = false;
                }
                else
                {
                    _playback.VolumeScale = _failGainCurve.Evaluate(
                        elapsed / _failDuration);
                }
            }

            if (_syncController.state == AudioTimeSyncController.State.Paused
                || _syncController.state == AudioTimeSyncController.State.Stopped)
            {
                if (_playback.IsPlaying)
                    _playback.Pause();
            }
        }

        public void StartOverridePlayback(AudioTimeSyncController syncController,
            string overridePath, string referenceAudioPath, float songTimeOffset)
        {
            Cleanup();

            try
            {
                _syncController = syncController;
                _songTimeOffset = songTimeOffset;
                _audioLatency = (float)Traverse.Create(syncController)
                    .Field("_audioLatency").GetValue();

                var gameSource = (AudioSource)Traverse.Create(syncController)
                    .Field("_audioSource").GetValue();

                if (gameSource != null)
                {
                    _mutedGameSource = gameSource;
                    _originalVolume = gameSource.volume;
                    _originalMute = gameSource.mute;
                    _gameMixerGroup = gameSource.outputAudioMixerGroup;
                    gameSource.mute = true;
                    gameSource.volume = 0f;
                    Plugin.Log.Info("Muted game AudioSource");
                }

                var clip = SidecarPlayback.LoadAudioAsAudioClip(overridePath);
                _clipLeadInCompensation = AudioAlignment
                    .EstimateLeadInCompensation(referenceAudioPath, overridePath);
                _playback = new SidecarPlayback();
                float startTime = GetTargetPlaybackTime();
                _playback.Prepare(clip, startTime, _gameMixerGroup);
                _overrideActive = true;
                _playbackStarted = false;

                if (CanStartPreparedPlayback())
                {
                    _playback.Start();
                    _playbackStarted = true;
                }

                Plugin.Log.Info(
                    $"Override playback started (offset={_songTimeOffset}s, latency={_audioLatency}s, leadInComp={_clipLeadInCompensation}s)");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Override playback failed: {ex}");
                Cleanup();
            }
        }

        public void PauseOverride()
        {
            if (_playback != null && _overrideActive)
                _playback.Pause();
        }

        public void ResumeOverride()
        {
            if (_playback != null && _overrideActive)
            {
                _playback.Resume();
                _playbackStarted = _playback.HasStarted;
            }
        }

        public void SeekOverride(float time)
        {
            if (_playback != null && _overrideActive)
                _playback.Time = GetTargetPlaybackTime(time);
        }

        public void StartFailAnimation(AudioSource gameSource,
            AnimationCurve gainCurve, float duration)
        {
            if (gameSource != _mutedGameSource || !_overrideActive)
                return;

            _failGainCurve = gainCurve;
            _failDuration = duration;
            _failStartTime = Time.time;
            _failAnimating = true;
        }

        private float GetTargetPlaybackTime()
        {
            return GetTargetPlaybackTime(_syncController != null
                ? _syncController.songTime
                : 0f);
        }

        private float GetTargetPlaybackTime(float fallbackSongTime)
        {
            return Mathf.Max(0f,
                fallbackSongTime + _songTimeOffset + _audioLatency
                - _clipLeadInCompensation);
        }

        private bool CanStartPreparedPlayback()
        {
            if (_playback == null || !_playback.IsPrepared)
                return false;

            if (_syncController == null
                || _syncController.state != AudioTimeSyncController.State.Playing)
                return false;

            return GetTargetPlaybackTime() > 0f;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_overrideActive)
            {
                Plugin.Log.Info("Scene unloaded, cleaning up override playback");
                Cleanup();
            }
        }

        private void Cleanup()
        {
            if (_mutedGameSource != null)
            {
                _mutedGameSource.mute = _originalMute;
                _mutedGameSource.volume = _originalVolume;
                _mutedGameSource = null;
            }

            _playback?.Dispose();
            _playback = null;
            _syncController = null;
            _overrideActive = false;
            _playbackStarted = false;
            _songTimeOffset = 0f;
            _audioLatency = 0f;
            _clipLeadInCompensation = 0f;
            _originalMute = false;
            _gameMixerGroup = null;
            _lastPitch = 1f;
            _failGainCurve = null;
            _failDuration = 0f;
            _failStartTime = 0f;
            _failAnimating = false;
            HarmonyPatches.ClearPending();
        }
    }
}
