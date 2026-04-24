using System;
using NAudio.Wave;
using UnityEngine;
using UnityEngine.Audio;

namespace BSLossless
{
    public class SidecarPlayback : IDisposable
    {
        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private GameObject _gameObject;
        private bool _disposed;
        private bool _started;
        private float _volumeScale = 1f;

        public float VolumeScale
        {
            get => _volumeScale;
            set
            {
                _volumeScale = value;
                if (_audioSource != null)
                    _audioSource.volume = _volumeScale;
            }
        }

        public bool IsPlaying => _audioSource != null && _audioSource.isPlaying;
        public float Length => _audioClip != null ? _audioClip.length : 0f;
        public bool IsPrepared => _audioSource != null && _audioClip != null;
        public bool HasStarted => _started;

        public float Speed
        {
            get => _audioSource != null ? _audioSource.pitch : 1f;
            set { if (_audioSource != null) _audioSource.pitch = value; }
        }

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

        public void Prepare(AudioClip clip, float startTime = 0f,
            AudioMixerGroup mixerGroup = null)
        {
            Stop();
            _audioClip = clip;
            _gameObject = new GameObject("BSLosslessPlayer");
            GameObject.DontDestroyOnLoad(_gameObject);
            _audioSource = _gameObject.AddComponent<AudioSource>();
            _audioSource.clip = _audioClip;
            _audioSource.loop = false;
            _audioSource.playOnAwake = false;
            _audioSource.volume = 1f;
            _audioSource.spatialBlend = 0f;
            _audioSource.outputAudioMixerGroup = mixerGroup;
            _audioSource.time = Mathf.Clamp(startTime, 0f, clip.length - 0.001f);
            _started = false;
        }

        public void Start()
        {
            if (_audioSource == null || _started)
                return;

            _audioSource.Play();
            _started = true;
        }

        public void Pause()
        {
            if (_audioSource != null)
                _audioSource.Pause();
        }

        public void Resume()
        {
            if (_audioSource == null)
                return;

            if (!_started)
            {
                Start();
                return;
            }

            if (!_audioSource.isPlaying)
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
            _started = false;
            _volumeScale = 1f;
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }

        public struct DecodedAudio
        {
            public float[] Samples;
            public int Channels;
            public int SampleRate;
            public int FrameCount;
        }

        public static DecodedAudio DecodeAudioData(string path)
        {
            Plugin.Log.Info($"Decoding audio: {path}");

            using (var reader = new AudioFileReader(path))
            {
                int channels = reader.WaveFormat.Channels;
                int sampleRate = reader.WaveFormat.SampleRate;
                double duration = reader.TotalTime.TotalSeconds;
                int totalFrames = (int)Math.Ceiling(duration * sampleRate);
                int totalSamples = totalFrames * channels;

                Plugin.Log.Debug($"Audio info: {sampleRate}Hz, {channels}ch, {duration:F1}s");

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

                Plugin.Log.Info($"Audio decoded: {actualFrames} frames, {duration:F1}s");

                return new DecodedAudio
                {
                    Samples = clipSamples,
                    Channels = channels,
                    SampleRate = sampleRate,
                    FrameCount = actualFrames
                };
            }
        }

        public static AudioClip CreateAudioClip(DecodedAudio decoded)
        {
            var clip = AudioClip.Create("BSLossless", decoded.FrameCount,
                decoded.Channels, decoded.SampleRate, false);
            clip.SetData(decoded.Samples, 0);
            Plugin.Log.Info($"Audio clip created: {decoded.FrameCount} frames, {clip.length:F1}s");
            return clip;
        }

        public static AudioClip LoadAudioAsAudioClip(string path)
        {
            return CreateAudioClip(DecodeAudioData(path));
        }
    }
}
