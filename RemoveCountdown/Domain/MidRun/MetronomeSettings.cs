using System;

namespace RemoveCountdown.Domain.MidRun;

internal readonly struct MetronomeSettings : IEquatable<MetronomeSettings>
{
  internal const double MinimumClickBpm = 20.0;
  internal const double MaximumClickBpm = 999.0;
  internal const int MinimumMeterValue = 1;
  internal const int MaximumMeterValue = 16;

  internal MetronomeSettings(double clickBpm, int numerator, int denominator)
  {
    ClickBpm = NormalizeBpm(clickBpm);
    Numerator = Math.Clamp(numerator, MinimumMeterValue, MaximumMeterValue);
    Denominator = Math.Clamp(denominator, MinimumMeterValue, MaximumMeterValue);
  }

  internal double ClickBpm { get; }
  internal int Numerator { get; }
  internal int Denominator { get; }

  internal MetronomeSettings WithClickBpm(double value) => new(value, Numerator, Denominator);

  internal MetronomeSettings WithNumerator(int value) => new(ClickBpm, value, Denominator);

  internal MetronomeSettings WithDenominator(int value) => new(ClickBpm, Numerator, value);

  public bool Equals(MetronomeSettings other) =>
    ClickBpm.Equals(other.ClickBpm) && Numerator == other.Numerator && Denominator == other.Denominator;

  public override bool Equals(object obj) => obj is MetronomeSettings other && Equals(other);

  public override int GetHashCode() => HashCode.Combine(ClickBpm, Numerator, Denominator);

  public static bool operator ==(MetronomeSettings left, MetronomeSettings right) => left.Equals(right);

  public static bool operator !=(MetronomeSettings left, MetronomeSettings right) => !left.Equals(right);

  private static double NormalizeBpm(double value)
  {
    if (double.IsNaN(value) || double.IsInfinity(value))
    {
      return MinimumClickBpm;
    }
    return Math.Clamp(Math.Round(value, 1, MidpointRounding.AwayFromZero), MinimumClickBpm, MaximumClickBpm);
  }
}
