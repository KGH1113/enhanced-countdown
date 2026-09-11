using System;
using System.Reflection;
using HarmonyLib;

namespace EnhancedCountdown.Infrastructure.Compatibility;

internal static class AdofaiRuntimeApi
{
  private static readonly MethodInfo SignedTickPlayerUpdateMethod = AccessTools.Method(
    typeof(scrPlayer),
    nameof(scrPlayer.Simulated_PlayerControl_Update),
    new[] { typeof(long?) }
  );
  private static readonly MethodInfo UnsignedTickPlayerUpdateMethod = AccessTools.Method(
    typeof(scrPlayer),
    nameof(scrPlayer.Simulated_PlayerControl_Update),
    new[] { typeof(ulong?) }
  );
  private static readonly MethodInfo SignedTickHitMethod = AccessTools.Method(
    typeof(scrPlayer),
    nameof(scrPlayer.Hit),
    new[] { typeof(long?), typeof(bool) }
  );
  private static readonly MethodInfo UnsignedTickHitMethod = AccessTools.Method(
    typeof(scrPlayer),
    nameof(scrPlayer.Hit),
    new[] { typeof(ulong?), typeof(bool) }
  );
  private static readonly MethodInfo LegacyHitMethod = AccessTools.Method(
    typeof(scrPlayer),
    nameof(scrPlayer.Hit),
    new[] { typeof(bool) }
  );
  private static readonly FieldInfo PreviousFrameTickField = RequiredTickField("prevFrameTick");
  private static readonly FieldInfo CurrentFrameTickField = RequiredTickField("currFrameTick");
  private static readonly FieldInfo OffsetTickField = RequiredTickField("offsetTick");

  internal static MethodInfo PlayerUpdateMethod =>
    SignedTickPlayerUpdateMethod
    ?? UnsignedTickPlayerUpdateMethod
    ?? throw new MissingMethodException(typeof(scrPlayer).FullName, nameof(scrPlayer.Simulated_PlayerControl_Update));

  internal static MethodInfo PlayerHitMethod =>
    SignedTickHitMethod
    ?? UnsignedTickHitMethod
    ?? LegacyHitMethod
    ?? throw new MissingMethodException(typeof(scrPlayer).FullName, nameof(scrPlayer.Hit));

  internal static bool Hit(scrPlayer player, bool isAuto)
  {
    MethodInfo method = PlayerHitMethod;
    object[] arguments = method.GetParameters().Length == 2 ? new object[] { null, isAuto } : new object[] { isAuto };
    return (bool)method.Invoke(player, arguments);
  }

  internal static void SetAsyncInputClock(long nowTick, long offsetTick)
  {
    SetTick(PreviousFrameTickField, nowTick);
    SetTick(CurrentFrameTickField, nowTick);
    SetTick(OffsetTickField, offsetTick);
  }

  private static FieldInfo RequiredTickField(string name)
  {
    FieldInfo field = AccessTools.Field(typeof(AsyncInputManager), name);
    if (field == null || (field.FieldType != typeof(long) && field.FieldType != typeof(ulong)))
    {
      throw new MissingFieldException(typeof(AsyncInputManager).FullName, name);
    }
    return field;
  }

  private static void SetTick(FieldInfo field, long value)
  {
    object runtimeValue = field.FieldType == typeof(long) ? value : checked((ulong)value);
    field.SetValue(null, runtimeValue);
  }
}
