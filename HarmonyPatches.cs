using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace BSSidecarAudio
{
    [HarmonyPatch]
    static class HarmonyPatches
    {
        internal static string PendingFlacPath { get; private set; }
        internal static string PendingAudioPath { get; private set; }
        internal static float PendingSongTimeOffset { get; private set; }
        internal static bool HasFlac => !string.IsNullOrEmpty(PendingFlacPath);

        private static readonly Dictionary<string, AudioClip> _previewClipCache
            = new Dictionary<string, AudioClip>();
        private static readonly List<string> _previewCacheOrder = new List<string>();
        private const int MaxPreviewCacheSize = 3;

        internal static void ClearPending()
        {
            PendingFlacPath = null;
            PendingAudioPath = null;
            PendingSongTimeOffset = 0f;
        }

        internal static void ClearPreviewCache()
        {
            foreach (var kvp in _previewClipCache)
            {
                if (kvp.Value != null
                    && kvp.Value.loadState == AudioDataLoadState.Loaded)
                    kvp.Value.UnloadAudioData();
            }
            _previewClipCache.Clear();
            _previewCacheOrder.Clear();
        }

        [HarmonyPatch(typeof(GameplayCoreInstaller), "InstallBindings")]
        [HarmonyPostfix]
        static void GameplayCoreInstaller_Start(GameplayCoreInstaller __instance)
        {
            ClearPending();

            if (!Configuration.PluginConfig.Instance.Enabled)
            {
                Plugin.Log.Debug("Sidecar disabled in config");
                return;
            }

            try
            {
                var setupData = (GameplayCoreSceneSetupData)Traverse.Create(__instance)
                    .Field("_sceneSetupData").GetValue();

                if (setupData == null)
                {
                    Plugin.Log.Warn("GameplayCoreSceneSetupData is null");
                    return;
                }

                var levelData = (IBeatmapLevelData)Traverse.Create(setupData)
                    .Property("beatmapLevelData").GetValue();

                var beatmapLevel = setupData.beatmapLevel;
                float songTimeOffset = beatmapLevel?.songTimeOffset ?? 0f;

                if (levelData is FileSystemBeatmapLevelData fsLevelData)
                {
                    string audioPath = (string)Traverse.Create(fsLevelData)
                        .Field("_audioClipPath").GetValue();

                    if (string.IsNullOrEmpty(audioPath))
                    {
                        Plugin.Log.Warn("audioClipPath is empty");
                        return;
                    }

                    string levelDir = Path.GetDirectoryName(audioPath);
                    string flacPath = Path.Combine(levelDir, "song.flac");

                    if (File.Exists(flacPath))
                    {
                        Plugin.Log.Info($"Found song.flac: {flacPath} (offset={songTimeOffset}s)");
                        PendingFlacPath = flacPath;
                        PendingAudioPath = audioPath;
                        PendingSongTimeOffset = songTimeOffset;
                    }
                    else
                    {
                        Plugin.Log.Debug($"No song.flac in {levelDir}");
                    }
                }
                else
                {
                    Plugin.Log.Debug(
                        $"Level data type: {levelData?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error detecting FLAC: {ex}");
                ClearPending();
            }
        }

        [HarmonyPatch(typeof(AudioTimeSyncController), "StartSong")]
        [HarmonyPostfix]
        static void AudioTimeSyncController_StartSong(
            AudioTimeSyncController __instance)
        {
            if (!HasFlac) return;

            try
            {
                BSSidecarAudioController.Instance?.StartFlacPlayback(
                    __instance, PendingFlacPath, PendingAudioPath, PendingSongTimeOffset);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error starting FLAC playback: {ex}");
            }
        }

        [HarmonyPatch(typeof(AudioTimeSyncController), "Pause")]
        [HarmonyPostfix]
        static void AudioTimeSyncController_Pause()
        {
            BSSidecarAudioController.Instance?.PauseFlac();
        }

        [HarmonyPatch(typeof(AudioTimeSyncController), "Resume")]
        [HarmonyPostfix]
        static void AudioTimeSyncController_Resume()
        {
            BSSidecarAudioController.Instance?.ResumeFlac();
        }

        [HarmonyPatch(typeof(AudioTimeSyncController), "SeekTo")]
        [HarmonyPostfix]
        static void AudioTimeSyncController_SeekTo(
            AudioTimeSyncController __instance)
        {
            BSSidecarAudioController.Instance?.SeekFlac(__instance.songTime);
        }

        [HarmonyPatch(typeof(AudioPitchGainEffect), "StartEffect")]
        [HarmonyPostfix]
        static void AudioPitchGainEffect_StartEffect(
            AudioPitchGainEffect __instance)
        {
            var audioSource = (AudioSource)Traverse.Create(__instance)
                .Field("_audioSource").GetValue();
            var gainCurve = (AnimationCurve)Traverse.Create(__instance)
                .Field("_gainCurve").GetValue();
            var duration = (float)Traverse.Create(__instance)
                .Field("_duration").GetValue();

            BSSidecarAudioController.Instance?.StartFailAnimation(
                audioSource, gainCurve, duration);
        }

        [HarmonyPatch(typeof(FileSystemPreviewMediaData), "GetPreviewAudioClip")]
        [HarmonyPrefix]
        static bool PreviewMedia_GetPreviewAudioClip(
            ref Task<AudioClip> __result,
            FileSystemPreviewMediaData __instance)
        {
            if (!Configuration.PluginConfig.Instance.Enabled)
                return true;

            try
            {
                string previewPath = Traverse.Create(__instance)
                    .Field("_previewAudioClipPath").GetValue<string>();

                if (string.IsNullOrEmpty(previewPath))
                    return true;

                string levelDir = Path.GetDirectoryName(previewPath);
                string flacPath = Path.Combine(levelDir, "song.flac");

                if (!File.Exists(flacPath))
                    return true;

                if (_previewClipCache.TryGetValue(flacPath, out var cached))
                {
                    Plugin.Log.Debug(
                        $"Preview: cached FLAC for {Path.GetFileName(levelDir)}");
                    __result = Task.FromResult(cached);
                    return false;
                }

                var clip = SidecarPlayback.LoadFlacAsAudioClip(flacPath);

                while (_previewClipCache.Count >= MaxPreviewCacheSize
                    && _previewCacheOrder.Count > 0)
                {
                    string oldest = _previewCacheOrder[0];
                    _previewCacheOrder.RemoveAt(0);
                    if (_previewClipCache.TryGetValue(oldest, out var oldClip))
                    {
                        _previewClipCache.Remove(oldest);
                        if (oldClip != null
                            && oldClip.loadState == AudioDataLoadState.Loaded)
                            oldClip.UnloadAudioData();
                    }
                }

                _previewClipCache[flacPath] = clip;
                _previewCacheOrder.Add(flacPath);

                Plugin.Log.Info(
                    $"Preview: loaded FLAC for {Path.GetFileName(levelDir)}");
                __result = Task.FromResult(clip);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Preview FLAC substitution failed: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(FileSystemPreviewMediaData), "UnloadPreviewAudioClip")]
        [HarmonyPostfix]
        static void PreviewMedia_UnloadPreviewAudioClip(
            FileSystemPreviewMediaData __instance)
        {
            try
            {
                string previewPath = Traverse.Create(__instance)
                    .Field("_previewAudioClipPath").GetValue<string>();

                if (string.IsNullOrEmpty(previewPath))
                    return;

                string levelDir = Path.GetDirectoryName(previewPath);
                string flacPath = Path.Combine(levelDir, "song.flac");

                if (_previewClipCache.TryGetValue(flacPath, out var clip))
                {
                    _previewClipCache.Remove(flacPath);
                    _previewCacheOrder.Remove(flacPath);
                    if (clip != null
                        && clip.loadState == AudioDataLoadState.Loaded)
                        clip.UnloadAudioData();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Preview FLAC cleanup failed: {ex}");
            }
        }
    }
}
