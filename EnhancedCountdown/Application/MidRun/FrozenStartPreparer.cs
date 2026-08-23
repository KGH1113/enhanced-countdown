using System;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.MidRun;

internal sealed class FrozenStartPreparer
{
  private readonly IGameWorld gameWorld;
  private readonly IAudioTimeline audioTimeline;
  private readonly IHitSoundScheduler hitSounds;
  private readonly IMetronome metronome;
  private readonly IFrozenVisuals visuals;
  private readonly IModLogger logger;

  internal FrozenStartPreparer(
    IGameWorld gameWorld,
    IAudioTimeline audioTimeline,
    IHitSoundScheduler hitSounds,
    IMetronome metronome,
    IFrozenVisuals visuals,
    IModLogger logger
  )
  {
    this.gameWorld = gameWorld;
    this.audioTimeline = audioTimeline;
    this.hitSounds = hitSounds;
    this.metronome = metronome;
    this.visuals = visuals;
    this.logger = logger;
  }

  internal bool Prepare(FrozenStartSession session)
  {
    session.Phase = FrozenStartPhase.Preparing;
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
      logger.Log("The selected start has no following manual tile; continuing without a start freeze.");
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
      logger.Log("The prepared hit-sound schedule is unavailable; continuing without a frozen start.");
      return false;
    }
    visuals.HideStartUi(session.Controller);

    session.WarmupStartedFrame = gameWorld.CurrentFrame;
    session.WarmupStartedRealtime = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
    session.Phase = FrozenStartPhase.Warming;
    logger.Log(
      $"Warming frozen editor start at tile {gameWorld.GetCurrentFloorId(primary)}, "
        + $"song time {session.FrozenSongPosition:F6}, audio time {session.FrozenAudioSongPosition:F6}, "
        + "with gameplay and audio paused."
    );
    return true;
  }

  private double CalculatePerfectSongPosition(scrPlayer player)
  {
    PerfectTimingInput input = gameWorld.GetPerfectTimingInput(player, audioTimeline.Crotchet);
    return FrozenStartTiming.CalculatePerfectSongPosition(input);
  }
}
