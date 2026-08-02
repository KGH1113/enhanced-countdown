using System.IO;
using Newtonsoft.Json;

namespace EnhancedCountdown.Infrastructure.Settings;

internal sealed class UpdateSettings
{
  public bool ReceiveBetaUpdates { get; set; }

  public static UpdateSettings Load(string path)
  {
    try
    {
      if (!File.Exists(path))
        return new UpdateSettings();
      return JsonConvert.DeserializeObject<UpdateSettings>(File.ReadAllText(path)) ?? new UpdateSettings();
    }
    catch
    {
      return new UpdateSettings();
    }
  }

  public void Save(string path)
  {
    File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented) + System.Environment.NewLine);
  }
}
