using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;
using System.Reflection;

namespace BSSidecarAudio
{
    public class BSSidecarAudioController : MonoBehaviour
    {
        public static BSSidecarAudioController Instance { get; private set; }

        private SidecarPlayback _playback;
        private AudioTimeSyncController _syncController;
        private AudioSource _mutedGameSource;
        private float _originalVolume;
        private bool _originalMute;
        private bool _flacActive;
        private float _songTimeOffset;

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
            if (Instance == this) Instance = null;
            Plugin.Log.Debug($"{name}: OnDestroy()");
        }

        private void LateUpdate()
        {
            if (!_flacActive || _playback == null || _syncController == null)
                return;

            float targetTime = _syncController.songTime - _songTimeOffset;
            float currentTime = _playback.Time;
            float diff = targetTime - currentTime;
            float threshold = Configuration.PluginConfig.Instance?.SyncThreshold
                ?? 0.05f;

            if (Mathf.Abs(diff) > threshold)
                _playback.Time = targetTime;

            if (_syncController.state == AudioTimeSyncController.State.Paused
                || _syncController.state == AudioTimeSyncController.State.Stopped)
            {
                if (_playback.IsPlaying)
                    _playback.Pause();
            }
        }

        public void StartFlacPlayback(AudioTimeSyncController syncController,
            string flacPath, float songTimeOffset)
        {
            Cleanup();

            try
            {
                _syncController = syncController;
                _songTimeOffset = songTimeOffset;

                var gameSource = (AudioSource)Traverse.Create(syncController)
                    .Field("_audioSource").GetValue();

                if (gameSource != null)
                {
                    _mutedGameSource = gameSource;
                    _originalVolume = gameSource.volume;
                    _originalMute = gameSource.mute;
                    gameSource.mute = true;
                    gameSource.volume = 0f;
                    Plugin.Log.Info("Muted game AudioSource");
                }

                var clip = SidecarPlayback.LoadFlacAsAudioClip(flacPath);
                _playback = new SidecarPlayback();
                float startTime = syncController.songTime - _songTimeOffset;
                _playback.Play(clip, startTime);
                _flacActive = true;

                Plugin.Log.Info($"FLAC playback started (offset={_songTimeOffset}s)");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"FLAC playback failed: {ex}");
                Cleanup();
            }
        }

        public void PauseFlac()
        {
            if (_playback != null && _flacActive)
                _playback.Pause();
        }

        public void ResumeFlac()
        {
            if (_playback != null && _flacActive)
                _playback.Resume();
        }

        public void SeekFlac(float time)
        {
            if (_playback != null && _flacActive)
                _playback.Time = time - _songTimeOffset;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (_flacActive)
            {
                Plugin.Log.Info("Scene unloaded, cleaning up FLAC playback");
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
            _flacActive = false;
            _songTimeOffset = 0f;
            _originalMute = false;
            HarmonyPatches.ClearPending();
        }
    }
}
