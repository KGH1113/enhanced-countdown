using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace RemoveCountdown.Launcher;

internal sealed class RuntimeStore
{
  private static readonly Regex VersionPattern = new(
    "\\\"Version\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"",
    RegexOptions.CultureInvariant
  );

  private readonly string installPath;
  private readonly string runtimeRoot;
  private readonly string versionsRoot;
  private readonly string statePath;

  public RuntimeStore(string installPath)
  {
    this.installPath = Path.GetFullPath(installPath ?? throw new ArgumentNullException(nameof(installPath)));
    runtimeRoot = Path.Combine(this.installPath, "Runtime");
    versionsRoot = Path.Combine(runtimeRoot, "versions");
    statePath = Path.Combine(runtimeRoot, "state.json");
  }

  public RuntimeState LoadAndRepair()
  {
    string backup = statePath + ".bak";
    if (!File.Exists(statePath) && File.Exists(backup))
      File.Move(backup, statePath);
    if (!File.Exists(statePath))
      throw new InvalidDataException("RemoveCountdown Runtime/state.json is missing. Install version 0.2.0 manually.");

    RuntimeState state =
      JsonConvert.DeserializeObject<RuntimeState>(File.ReadAllText(statePath))
      ?? throw new InvalidDataException("RemoveCountdown runtime state is empty.");
    if (state.SchemaVersion != 2 || string.IsNullOrWhiteSpace(state.Current))
      throw new InvalidDataException("RemoveCountdown runtime state is invalid.");

    if (!string.IsNullOrWhiteSpace(state.Trial))
    {
      string abandonedTrial = state.Trial;
      Reject(state, abandonedTrial, DateTime.UtcNow);
      DeleteUnreferencedRuntime(abandonedTrial, state);
      Save(state);
    }

    try
    {
      GetCandidate(state.Current);
    }
    catch when (!string.IsNullOrWhiteSpace(state.Previous))
    {
      GetCandidate(state.Previous);
      state.Current = state.Previous;
      state.Previous = null;
      Save(state);
    }

    CleanupLegacyPayload();
    CleanupVersions(state);
    return state;
  }

  public RuntimeCandidate GetCandidate(string version)
  {
    if (string.IsNullOrWhiteSpace(version))
      throw new InvalidDataException("The runtime version is missing.");
    return ValidateCandidate(version, Path.Combine(versionsRoot, NormalizeVersion(version)));
  }

  public RuntimeCandidate ValidateCandidate(string version, string runtimePath)
  {
    string expected = Path.GetFullPath(Path.Combine(versionsRoot, NormalizeVersion(version)));
    string actual = Path.GetFullPath(runtimePath ?? string.Empty);
    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException("The update engine returned an unexpected runtime path.");

    string assembly = Path.Combine(actual, "RemoveCountdown.dll");
    string engine = Path.Combine(actual, "RemoveCountdown.UpdateEngine.dll");
    string info = Path.Combine(actual, "Info.json");
    if (!File.Exists(assembly) || !File.Exists(engine) || !File.Exists(info))
      throw new InvalidDataException("The runtime is incomplete.");

    Match match = VersionPattern.Match(File.ReadAllText(info));
    if (!match.Success || !VersionsEqual(match.Groups[1].Value, version))
      throw new InvalidDataException("The runtime Info.json version is invalid.");
    return new RuntimeCandidate(NormalizeVersion(version), actual);
  }

  public void Promote(RuntimeState state, string version)
  {
    string normalized = NormalizeVersion(version);
    if (!VersionsEqual(state.Current, normalized))
      state.Previous = state.Current;
    state.Current = normalized;
    state.Trial = null;
    state.RejectedVersion = null;
    state.FailureCount = 0;
    state.LastFailureUtc = null;
    Save(state);
    CleanupVersions(state);
  }

  public void Reject(RuntimeState state, string version, DateTime failedAtUtc)
  {
    string normalized = NormalizeVersion(version);
    state.FailureCount = VersionsEqual(state.RejectedVersion, normalized) ? checked(state.FailureCount + 1) : 1;
    state.RejectedVersion = normalized;
    state.LastFailureUtc = failedAtUtc.ToUniversalTime().ToString("O");
    state.Trial = null;
  }

  public void Save(RuntimeState state)
  {
    Directory.CreateDirectory(runtimeRoot);
    string temporary = statePath + ".tmp";
    string backup = statePath + ".bak";
    File.WriteAllText(
      temporary,
      JsonConvert.SerializeObject(state, Formatting.Indented) + Environment.NewLine,
      Encoding.UTF8
    );
    if (File.Exists(statePath))
    {
      if (File.Exists(backup))
        File.Delete(backup);
      File.Replace(temporary, statePath, backup, true);
      TryDeleteFile(backup);
    }
    else
    {
      File.Move(temporary, statePath);
    }
  }

  public void DeleteUnreferencedRuntime(string version, RuntimeState state)
  {
    if (
      string.IsNullOrWhiteSpace(version)
      || VersionsEqual(version, state.Current)
      || VersionsEqual(version, state.Previous)
      || VersionsEqual(version, state.Trial)
    )
      return;
    TryDeleteDirectory(Path.Combine(versionsRoot, NormalizeVersion(version)));
  }

  private void CleanupVersions(RuntimeState state)
  {
    if (!Directory.Exists(versionsRoot))
      return;
    foreach (string directory in Directory.GetDirectories(versionsRoot))
    {
      string name = Path.GetFileName(directory);
      if (VersionsEqual(name, state.Current) || VersionsEqual(name, state.Previous) || VersionsEqual(name, state.Trial))
        continue;
      TryDeleteDirectory(directory);
    }
  }

  private void CleanupLegacyPayload()
  {
    TryDeleteFile(Path.Combine(installPath, "RemoveCountdown.dll"));
    TryDeleteFile(Path.Combine(installPath, "RemoveCountdown.pdb"));
    TryDeleteFile(Path.Combine(installPath, "RemoveCountdown.deps.json"));
  }

  private static string NormalizeVersion(string version)
  {
    string normalized = version?.Trim().TrimStart('v', 'V');
    if (
      string.IsNullOrWhiteSpace(normalized)
      || normalized.IndexOfAny(new[] { '/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0
      || normalized == "."
      || normalized == ".."
    )
      throw new InvalidDataException("The runtime version is invalid.");
    return normalized;
  }

  private static bool VersionsEqual(string left, string right)
  {
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
      return false;
    return string.Equals(NormalizeVersion(left), NormalizeVersion(right), StringComparison.OrdinalIgnoreCase);
  }

  private static void TryDeleteDirectory(string path)
  {
    try
    {
      if (Directory.Exists(path))
        Directory.Delete(path, true);
    }
    catch { }
  }

  private static void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch { }
  }
}
