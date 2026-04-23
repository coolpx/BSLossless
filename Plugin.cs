using IPA;
using IPA.Config;
using IPA.Config.Stores;
using UnityEngine;
using HarmonyLib;
using IPALogger = IPA.Logging.Logger;

namespace BSSidecarAudio
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static Plugin Instance { get; private set; }
        internal static IPALogger Log { get; private set; }
        private Harmony _harmony;

        [Init]
        public void Init(IPALogger logger, Config conf)
        {
            Instance = this;
            Log = logger;
            Configuration.PluginConfig.Instance = conf.Generated<Configuration.PluginConfig>();
            _harmony = new Harmony("com.coolpixels.bssidecaraudio");
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.Info("BSSidecarAudio initialized.");
        }

        [OnStart]
        public void OnApplicationStart()
        {
            new GameObject("BSSidecarAudioController").AddComponent<BSSidecarAudioController>();
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
