using System;
using System.Diagnostics;
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
    Stopwatch watch = Stopwatch.StartNew();
    FrozenStartTimings timings = session.Timings;
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

    double phaseStarted = watch.Elapsed.TotalMilliseconds;
    int safetyLimit = gameWorld.AutomaticTileSafetyLimit;
    while (safetyLimit-- > 0 && gameWorld.IsNextTileAutomatic(primary))
    {
      double automaticHitTime = CalculatePerfectSongPosition(primary);
      gameWorld.SeekLoadedWorld(automaticHitTime, automaticHitTime);
      gameWorld.AdvanceAutomaticTiles();
      timings.AutomaticTiles++;
    }
    timings.AutomaticTilesMilliseconds = watch.Elapsed.TotalMilliseconds - phaseStarted;

    if (safetyLimit <= 0)
    {
      throw new InvalidOperationException("Automatic tile preparation exceeded the floor count.");
    }

    if (!gameWorld.HasFollowingTile(primary))
    {
      session.FrozenSongPosition = audioTimeline.CurrentSongPosition;
      audioTimeline.PrimeSongSources(CalibratedAudioSongPosition(session.FrozenSongPosition));
      return false;
    }

    session.FrozenSongPosition = CalculatePerfectSongPosition(primary);
    session.FrozenAudioSongPosition = CalibratedAudioSongPosition(session.FrozenSongPosition);
    gameWorld.SeekLoadedWorld(session.FrozenSongPosition, session.FrozenAudioSongPosition);

    phaseStarted = watch.Elapsed.TotalMilliseconds;
    visuals.ScrubToTime(session.FrozenSongPosition);
    timings.VisualScrubMilliseconds = watch.Elapsed.TotalMilliseconds - phaseStarted;

    phaseStarted = watch.Elapsed.TotalMilliseconds;
    audioTimeline.PrimeSongSources(session.FrozenAudioSongPosition);
    timings.AudioPrimeMilliseconds = watch.Elapsed.TotalMilliseconds - phaseStarted;

    phaseStarted = watch.Elapsed.TotalMilliseconds;
    bool ready =
      audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition)
      && hitSounds.Prepare(gameWorld.GetNextFloorIndex(primary));
    timings.HitSoundPrepareMilliseconds = watch.Elapsed.TotalMilliseconds - phaseStarted;
    if (!ready)
    {
      return false;
    }
    visuals.HideStartUi(session.Controller);

    session.WarmupStartedFrame = gameWorld.CurrentFrame;
    session.Phase = FrozenStartPhase.Warming;
    timings.PrepareMilliseconds = watch.Elapsed.TotalMilliseconds;
    return true;
  }

  private double CalibratedAudioSongPosition(double logicalSongPosition)
  {
    return FrozenStartTiming.CalculateCalibratedAudioSongPosition(
      logicalSongPosition,
      audioTimeline.Calibration,
      audioTimeline.Pitch
    );
  }

  private double CalculatePerfectSongPosition(scrPlayer player)
  {
    PerfectTimingInput input = gameWorld.GetPerfectTimingInput(player, audioTimeline.Crotchet);
    return FrozenStartTiming.CalculatePerfectSongPosition(input);
  }
}
