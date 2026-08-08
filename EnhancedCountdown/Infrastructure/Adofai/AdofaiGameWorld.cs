using System;
using System.Collections.Generic;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;
using EnhancedCountdown.Infrastructure.Compatibility;
using UnityEngine;

namespace EnhancedCountdown.Infrastructure.Adofai;

internal sealed class AdofaiGameWorld : IGameWorld
{
  internal AdofaiGameWorld(IModLogger logger)
  {
    this.logger = logger;
  }

  private readonly IModLogger logger;

  public int CurrentFrame => Time.frameCount;
  public int AutomaticTileSafetyLimit => Math.Max(1, ADOBase.lm.listFloors.Count + 1);
  public bool IsAsyncInputActive => AsyncInputManager.isActive;

  public int ResolveStartFloor(int requestedFloor)
  {
    return requestedFloor >= 0 ? requestedFloor : GCS.checkpointNum;
  }

  public string GetNativeCountdownFallbackReason()
  {
    if (RDC.auto)
    {
      return "autoplay is active";
    }
    if (TufReplayPlaybackDetector.IsPlaybackActive())
    {
      return "TUFReplay playback is active";
    }
    return null;
  }

  public bool CanArm(scrController controller, int startFloor)
  {
    return ADOBase.isLevelEditor && startFloor > 0 && controller != null && controller == ADOBase.controller;
  }

  public bool CanPrepareInitialScrub(scrController controller, int floorNumber)
  {
    return controller != null && ADOBase.isLevelEditor && floorNumber > 0 && floorNumber == GCS.checkpointNum;
  }

  public bool CanHandleMusicScheduled(scrController controller)
  {
    return controller != null && controller == ADOBase.controller && ADOBase.isLevelEditor && GCS.checkpointNum > 0;
  }

  public bool IsRuntimeValid(scrController controller)
  {
    return ADOBase.isLevelEditor
      && GCS.checkpointNum > 0
      && controller != null
      && controller == ADOBase.controller
      && ADOBase.conductor != null;
  }

  public bool IsPauseRequest(scrController controller)
  {
    return controller != null && !controller.paused;
  }

  public void EnterPlayerControl(scrController controller)
  {
    controller.ChangeState(States.PlayerControl);
  }

  public scrPlayer GetPrimaryPlayer(scrController controller)
  {
    return controller?.playerOne;
  }

  public bool HasChosenPlanet(scrPlayer player)
  {
    return player?.planetarySystem?.chosenPlanet != null;
  }

  public bool IsNextTileAutomatic(scrPlayer player)
  {
    scrFloor next = player?.currFloor?.nextfloor;
    return next != null && next.auto;
  }

  public void AdvanceAutomaticTiles()
  {
    bool previousAuto = RDC.auto;
    RDC.auto = true;
    try
    {
      foreach (scrPlayer player in ADOBase.playerManager)
      {
        if (player?.currFloor?.nextfloor != null && player.currFloor.nextfloor.auto)
        {
          player.keyTimes.Clear();
          if (!player.Hit(isAuto: true))
          {
            throw new InvalidOperationException(
              $"Could not advance automatic tile {player.currFloor.nextfloor.seqID}."
            );
          }
        }
      }
    }
    finally
    {
      RDC.auto = previousAuto;
    }
  }

  public bool HasFollowingTile(scrPlayer player)
  {
    return player?.currFloor?.nextfloor != null;
  }

  public int GetCurrentFloorId(scrPlayer player)
  {
    return player.currFloor.seqID;
  }

  public PerfectTimingInput GetPerfectTimingInput(scrPlayer player, double crotchet)
  {
    scrPlanet planet = player?.planetarySystem?.chosenPlanet;
    if (planet == null || planet.player == null || planet.planetarySystem == null)
    {
      throw new InvalidOperationException("A valid chosen planet is required to calculate PP time.");
    }

    return new PerfectTimingInput(
      planet.player.lastHit,
      planet.targetExitAngle,
      planet.snappedLastAngle,
      planet.planetarySystem.isCW,
      crotchet,
      planet.planetarySystem.speed
    );
  }

  public void SeekLoadedWorld(double logicalSongPosition, double audioSongPosition)
  {
    scrConductor conductor = ADOBase.conductor;
    if (conductor == null || ADOBase.playerManager == null)
    {
      throw new InvalidOperationException("The conductor or player manager is unavailable.");
    }

    var lastHits = new Dictionary<scrPlayer, double>();
    foreach (scrPlayer player in ADOBase.playerManager)
    {
      lastHits[player] = player.lastHit;
    }

    conductor.dspTime = AudioSettings.dspTime;
    conductor.ScrubMusicToTime(audioSongPosition);
    conductor.songposition_minusi = logicalSongPosition;

    foreach (KeyValuePair<scrPlayer, double> pair in lastHits)
    {
      pair.Key.lastHit = pair.Value;
      pair.Key.planetarySystem?.chosenPlanet?.Update_RefreshAngles();
    }
  }

  public bool UnlockInputIfNeeded(scrPlayer player)
  {
    if (player.responsive || player.lockInput <= 0f)
    {
      return false;
    }

    player.UnlockInput();
    return true;
  }

  public bool ValidInputWasTriggered(scrPlayer player)
  {
    return player.ValidInputWasTriggered();
  }

  public string DescribeInput(scrPlayer player)
  {
    scrPlanet planet = player.planetarySystem?.chosenPlanet;
    return planet == null
      ? "Accepting frozen input."
      : $"Accepting frozen input at angle {planet.angle:F6}, "
        + $"target {planet.targetExitAngle:F6}, delta {planet.angle - planet.targetExitAngle:F6}, "
        + $"responsive {player.responsive}.";
  }

  public bool CanRetryHit(scrPlayer player)
  {
    return player.alive && player.currFloor != null && player.currFloor.nextfloor != null;
  }

  public bool Hit(scrPlayer player)
  {
    return player.Hit(isAuto: false);
  }

  public void UpdateInput(scrController controller)
  {
    controller.UpdateInput();
  }
}
