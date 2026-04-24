using System.Runtime.CompilerServices;
using IPA.Config.Stores;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]
namespace BSLossless.Configuration
{
    internal class PluginConfig
    {
        public static PluginConfig Instance { get; set; }
        public virtual bool Enabled { get; set; } = true;
        public virtual float SyncThreshold { get; set; } = 0.05f;
        public virtual void OnReload() { }
        public virtual void Changed() { }
        public virtual void CopyFrom(PluginConfig other)
        {
            Enabled = other.Enabled;
            SyncThreshold = other.SyncThreshold;
        }
    }
}
