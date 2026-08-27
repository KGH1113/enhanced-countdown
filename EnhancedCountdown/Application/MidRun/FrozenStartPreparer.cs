using System;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.MidRun;

internal sealed class FrozenStartPreparer
{
  private readonly IGameWorld gameWorld;
  private readonly IAudioTimeline audioTimeline;
  private readonly IHitSoundScheduler hitSounds;
  private readonly IFrozenVisuals visuals;

  internal FrozenStartPreparer(
    IGameWorld gameWorld,
    IAudioTimeline audioTimeline,
    IHitSoundScheduler hitSounds,
    IFrozenVisuals visuals
  )
  {
    this.gameWorld = gameWorld;
    this.audioTimeline = audioTimeline;
    this.hitSounds = hitSounds;
    this.visuals = visuals;
  }

  internal bool Prepare(FrozenStartSession session)
  {
    session.Phase = FrozenStartPhase.Preparing;
    visuals.ClearHitTexts();
    session.AudioSnapshot = audioTimeline.CaptureAndFreeze();
    session.HasAudioSnapshot = true;
    session.FrozenSongPosition = session.AudioSnapshot.SongPosition;

    gameWorld.EnterPlayerControl(session.Controller);
    visuals.HideStartUi(session.Controller);
    scrPlayer primary = gameWorld.GetPrimaryPlayer(session.Controller);
    if (!gameWorld.HasChosenPlanet(primary))
    {
      throw new InvalidOperationException("The primary player is unavailable.");
    }

    int safetyLimit = gameWorld.AutomaticTileSafetyLimit;
    while (safetyLimit-- > 0 && gameWorld.IsNextTileAutomatic(primary))
    {
      double automaticHitTime = CalculatePerfectSongPosition(primary);
      gameWorld.SeekLoadedWorld(automaticHitTime, automaticHitTime);
      gameWorld.AdvanceAutomaticTiles();
    }

    if (safetyLimit <= 0)
    {
      throw new InvalidOperationException("Automatic tile preparation exceeded the floor count.");
    }

    if (!gameWorld.HasFollowingTile(primary))
    {
      session.FrozenSongPosition = audioTimeline.CurrentSongPosition;
      return false;
    }

    session.FrozenSongPosition = CalculatePerfectSongPosition(primary);
    session.FrozenAudioSongPosition = FrozenStartTiming.CalculateCalibratedAudioSongPosition(
      session.FrozenSongPosition,
      audioTimeline.Calibration,
      audioTimeline.Pitch
    );
    gameWorld.SeekLoadedWorld(session.FrozenSongPosition, session.FrozenAudioSongPosition);
    visuals.ScrubToTime(session.FrozenSongPosition);
    audioTimeline.PrimeSongSources();
    audioTimeline.PauseListener();
    if (!audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition) || !hitSounds.Prepare())
    {
      return false;
    }
    visuals.HideStartUi(session.Controller);

    session.WarmupStartedFrame = gameWorld.CurrentFrame;
    session.Phase = FrozenStartPhase.Warming;
    return true;
  }

  private double CalculatePerfectSongPosition(scrPlayer player)
  {
    PerfectTimingInput input = gameWorld.GetPerfectTimingInput(player, audioTimeline.Crotchet);
    return FrozenStartTiming.CalculatePerfectSongPosition(input);
  }
}
