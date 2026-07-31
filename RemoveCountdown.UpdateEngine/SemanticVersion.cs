using System;
using System.Globalization;
using System.Linq;

namespace RemoveCountdown.UpdateEngine;

internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
  private readonly int[] core;
  private readonly string[] prerelease;

  private SemanticVersion(int[] core, string[] prerelease)
  {
    this.core = core;
    this.prerelease = prerelease;
  }

  public static SemanticVersion Parse(string value)
  {
    if (!TryParse(value, out SemanticVersion version))
      throw new FormatException("Invalid semantic version: " + value);
    return version;
  }

  public static bool TryParse(string value, out SemanticVersion version)
  {
    version = null;
    if (string.IsNullOrWhiteSpace(value))
      return false;
    string normalized = value.Trim().TrimStart('v', 'V');
    int buildSeparator = normalized.IndexOf('+');
    if (buildSeparator >= 0)
      normalized = normalized.Substring(0, buildSeparator);
    string prereleaseText = null;
    int prereleaseSeparator = normalized.IndexOf('-');
    if (prereleaseSeparator >= 0)
    {
      prereleaseText = normalized.Substring(prereleaseSeparator + 1);
      normalized = normalized.Substring(0, prereleaseSeparator);
    }

    string[] coreParts = normalized.Split('.');
    if (coreParts.Length is < 1 or > 4)
      return false;
    int[] core = new int[Math.Max(3, coreParts.Length)];
    for (int index = 0; index < coreParts.Length; index++)
    {
      if (
        !int.TryParse(coreParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out core[index])
        || core[index] < 0
      )
        return false;
    }

    string[] prerelease = Array.Empty<string>();
    if (prereleaseText != null)
    {
      prerelease = prereleaseText.Split('.');
      if (prerelease.Length == 0 || prerelease.Any(identifier => !IsValidIdentifier(identifier)))
        return false;
    }
    version = new SemanticVersion(core, prerelease);
    return true;
  }

  public int CompareTo(SemanticVersion other)
  {
    if (other == null)
      return 1;
    int coreLength = Math.Max(core.Length, other.core.Length);
    for (int index = 0; index < coreLength; index++)
    {
      int comparison = (index < core.Length ? core[index] : 0).CompareTo(
        index < other.core.Length ? other.core[index] : 0
      );
      if (comparison != 0)
        return comparison;
    }
    if (prerelease.Length == 0 || other.prerelease.Length == 0)
      return prerelease.Length == other.prerelease.Length ? 0 : (prerelease.Length == 0 ? 1 : -1);
    int length = Math.Min(prerelease.Length, other.prerelease.Length);
    for (int index = 0; index < length; index++)
    {
      int comparison = CompareIdentifier(prerelease[index], other.prerelease[index]);
      if (comparison != 0)
        return comparison;
    }
    return prerelease.Length.CompareTo(other.prerelease.Length);
  }

  public override string ToString()
  {
    string value = string.Join(".", core);
    return prerelease.Length == 0 ? value : value + "-" + string.Join(".", prerelease);
  }

  private static int CompareIdentifier(string left, string right)
  {
    bool leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber);
    bool rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber);
    if (leftNumeric && rightNumeric)
      return leftNumber.CompareTo(rightNumber);
    if (leftNumeric != rightNumeric)
      return leftNumeric ? -1 : 1;
    return string.CompareOrdinal(left, right);
  }

  private static bool IsValidIdentifier(string value)
  {
    return !string.IsNullOrWhiteSpace(value)
      && value.All(character => char.IsLetterOrDigit(character) || character == '-');
  }
}
