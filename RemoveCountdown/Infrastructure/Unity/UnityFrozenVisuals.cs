using System.Collections.Generic;
using DG.Tweening;
using RemoveCountdown.Application.Ports;

namespace RemoveCountdown.Infrastructure.Unity;

internal sealed class UnityFrozenVisuals : IFrozenVisuals
{
  private const float FrozenVisualLeadRadians = 0.017453292f;
  private readonly Dictionary<scrPlanet, float> frozenCosmeticAngles = new();
  private scrVfxPlus frozenVfx;

  public void HideStartUi(scrController controller)
  {
    controller.goShown = true;
    scrCountdown countdown = UnityEngine.Object.FindAnyObjectByType<scrCountdown>();
    countdown?.CancelGo();
    scrPressToStart prompt = UnityEngine.Object.FindAnyObjectByType<scrPressToStart>();
    prompt?.HideText();
  }

  public void ScrubToTime(double logicalSongPosition)
  {
    if (scrVfxPlus.instance == null)
    {
      return;
    }

    frozenVfx = scrVfxPlus.instance;
    frozenVfx.pausedTweens.Clear();
    frozenVfx.ScrubToTime((float)logicalSongPosition);
    foreach (Tween tween in frozenVfx.pausedTweens)
    {
      if (tween != null && tween.active)
      {
        tween.Pause();
      }
    }
  }

  public void ApplyPreLandingOffset()
  {
    frozenCosmeticAngles.Clear();
    foreach (scrPlayer player in ADOBase.playerManager)
    {
      scrPlanet planet = player?.planetarySystem?.chosenPlanet;
      if (planet == null)
      {
        continue;
      }

      frozenCosmeticAngles[planet] = planet.cosmeticAngle;
      float direction = planet.planetarySystem.isCW ? -1f : 1f;
      planet.cosmeticAngle += FrozenVisualLeadRadians * direction;
      planet.Update_RefreshAngles();
    }
  }

  public void RestorePlayer(scrPlayer player)
  {
    scrPlanet planet = player?.planetarySystem?.chosenPlanet;
    if (planet == null || !frozenCosmeticAngles.TryGetValue(planet, out float originalAngle))
    {
      return;
    }

    planet.cosmeticAngle = originalAngle;
    frozenCosmeticAngles.Remove(planet);
    planet.Update_RefreshAngles();
  }

  public void RestoreAll()
  {
    foreach (KeyValuePair<scrPlanet, float> pair in frozenCosmeticAngles)
    {
      if (pair.Key != null)
      {
        pair.Key.cosmeticAngle = pair.Value;
        pair.Key.Update_RefreshAngles();
      }
    }
    frozenCosmeticAngles.Clear();

    if (frozenVfx == null)
    {
      return;
    }

    foreach (Tween tween in frozenVfx.pausedTweens)
    {
      if (tween != null && tween.active)
      {
        tween.Play();
      }
    }
    frozenVfx.pausedTweens.Clear();
    frozenVfx = null;
  }
}
