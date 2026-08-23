using System;
using System.IO;
using Newtonsoft.Json;

namespace EnhancedCountdown.Infrastructure.Settings;

internal sealed class MetronomeAudioSettings
{
  internal const int DefaultVolumePercent = 100;
  internal const int MinimumVolumePercent = 0;
  internal const int MaximumVolumePercent = 100;

  public int VolumePercent { get; set; } = DefaultVolumePercent;

  public bool IsMuted { get; set; }

  internal static MetronomeAudioSettings Load(string path)
  {
    try
    {
      if (!File.Exists(path))
      {
        return new MetronomeAudioSettings();
      }

      MetronomeAudioSettings settings =
        JsonConvert.DeserializeObject<MetronomeAudioSettings>(File.ReadAllText(path)) ?? new MetronomeAudioSettings();
      settings.VolumePercent = NormalizeVolume(settings.VolumePercent);
      return settings;
    }
    catch
    {
      return new MetronomeAudioSettings();
    }
  }

  internal void Save(string path)
  {
    VolumePercent = NormalizeVolume(VolumePercent);
    string directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
    {
      Directory.CreateDirectory(directory);
    }
    File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented) + Environment.NewLine);
  }

  internal static int NormalizeVolume(int value) => Math.Clamp(value, MinimumVolumePercent, MaximumVolumePercent);
}
