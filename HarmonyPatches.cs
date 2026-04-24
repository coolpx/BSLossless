using System;
using System.IO;
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

        internal static void ClearPending()
        {
            PendingFlacPath = null;
            PendingAudioPath = null;
            PendingSongTimeOffset = 0f;
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
    }
}
