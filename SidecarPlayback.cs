using System;
using NAudio.Wave;
using UnityEngine;

namespace BSSidecarAudio
{
    public class SidecarPlayback : IDisposable
    {
        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private GameObject _gameObject;
        private bool _disposed;

        public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;
        public float Length => _audioClip != null ? _audioClip.length : 0f;

        public float Time
        {
            get => _audioSource != null ? _audioSource.time : 0f;
            set
            {
                if (_audioSource == null) return;
                if (_audioClip != null)
                    value = Mathf.Clamp(value, 0f, _audioClip.length - 0.001f);
                _audioSource.time = value;
            }
        }

        public void Play(AudioClip clip, float startTime = 0f)
        {
            Stop();
            _audioClip = clip;
            _gameObject = new GameObject("BSSidecarFlacPlayer");
            GameObject.DontDestroyOnLoad(_gameObject);
            _audioSource = _gameObject.AddComponent<AudioSource>();
            _audioSource.clip = _audioClip;
            _audioSource.loop = false;
            _audioSource.playOnAwake = false;
            _audioSource.volume = 1f;
            _audioSource.spatialBlend = 0f;
            _audioSource.time = Mathf.Clamp(startTime, 0f, clip.length - 0.001f);
            _audioSource.Play();
        }

        public void Pause()
        {
            if (_audioSource != null)
                _audioSource.Pause();
        }

        public void Resume()
        {
            if (_audioSource != null && !_audioSource.isPlaying)
                _audioSource.UnPause();
        }

        public void Stop()
        {
            if (_audioSource != null)
                _audioSource.Stop();
            if (_audioClip != null)
            {
                if (_audioClip.loadState == AudioDataLoadState.Loaded)
                    _audioClip.UnloadAudioData();
                _audioClip = null;
            }
            if (_gameObject != null)
            {
                GameObject.Destroy(_gameObject);
                _gameObject = null;
            }
            _audioSource = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }

        public static AudioClip LoadFlacAsAudioClip(string path)
        {
            Plugin.Log.Info($"Loading FLAC: {path}");

            using (var reader = new AudioFileReader(path))
            {
                int channels = reader.WaveFormat.Channels;
                int sampleRate = reader.WaveFormat.SampleRate;
                double duration = reader.TotalTime.TotalSeconds;
                int totalFrames = (int)Math.Ceiling(duration * sampleRate);
                int totalSamples = totalFrames * channels;

                Plugin.Log.Debug($"FLAC info: {sampleRate}Hz, {channels}ch, {duration:F1}s");

                float[] samples = new float[totalSamples];
                int totalRead = 0;
                int chunkSize = sampleRate * channels;
                int read;
                while (totalRead < totalSamples
                    && (read = reader.Read(samples, totalRead,
                        Math.Min(chunkSize, totalSamples - totalRead))) > 0)
                {
                    totalRead += read;
                }

                int actualFrames = totalRead / channels;
                float[] clipSamples = samples;
                if (totalRead != totalSamples)
                {
                    clipSamples = new float[totalRead];
                    Array.Copy(samples, clipSamples, totalRead);
                }

                var clip = AudioClip.Create("BSSidecarFlac", actualFrames,
                    channels, sampleRate, false);
                clip.SetData(clipSamples, 0);

                Plugin.Log.Info($"FLAC loaded: {actualFrames} frames, {clip.length:F1}s");
                return clip;
            }
        }
    }
}
