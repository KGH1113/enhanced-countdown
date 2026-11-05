using System;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;
using EnhancedCountdown.Infrastructure.Compatibility;
using UnityEngine;

namespace EnhancedCountdown.Infrastructure.Adofai;

internal sealed class AdofaiGameWorld : IGameWorld
{
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
    if (OrbitRenderActivityDetector.IsRendering())
    {
      return "OrbitRender is rendering";
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
          if (!AdofaiRuntimeApi.Hit(player, isAuto: true))
          {
            throw new InvalidOperationException(
              string.Concat("Could not advance automatic tile ", player.currFloor.nextfloor.seqID, ".")
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

  public int GetNextFloorIndex(scrPlayer player)
  {
    return player?.currFloor?.nextfloor?.seqID ?? -1;
  }

  public void SeekLoadedWorld(double logicalSongPosition, double audioSongPosition)
  {
    scrConductor conductor = ADOBase.conductor;
    if (conductor?.song == null || conductor.song.pitch == 0f || ADOBase.playerManager == null)
    {
      throw new InvalidOperationException("The conductor, song, or player manager is unavailable.");
    }

    // Same clock update as scrConductor.ScrubMusicToTime, without seeking the song or stopping sounds.
    // Seeking a streamed clip for every automatic tile is slow; the song is primed once at the final position.
    conductor.dspTime = AudioSettings.dspTime;
    conductor.dspTimeSong =
      conductor.dspTime - (audioSongPosition + conductor.addoffset) / conductor.song.pitch + 0.10000000149011612;
    conductor.songposition_minusi = logicalSongPosition;

    foreach (scrPlayer player in ADOBase.playerManager)
    {
      player.planetarySystem?.chosenPlanet?.Update_RefreshAngles();
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

  public double GetSecondsSinceInput(object inputTick)
  {
    return AdofaiRuntimeApi.GetSecondsSinceInputTick(inputTick);
  }

  public bool CanRetryHit(scrPlayer player)
  {
    return player.alive && player.currFloor != null && player.currFloor.nextfloor != null;
  }

  public bool Hit(scrPlayer player)
  {
    return AdofaiRuntimeApi.Hit(player, isAuto: false);
  }

  public void UpdateInput(scrController controller)
  {
    controller.UpdateInput();
  }
}
