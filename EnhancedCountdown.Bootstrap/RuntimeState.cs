namespace EnhancedCountdown.Launcher;

internal sealed class RuntimeState
{
  public int SchemaVersion { get; set; } = 2;
  public string Current { get; set; }
  public string Previous { get; set; }
  public string Trial { get; set; }
  public string RejectedVersion { get; set; }
  public int FailureCount { get; set; }
  public string LastFailureUtc { get; set; }
}
