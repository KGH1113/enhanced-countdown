using System;
using EnhancedCountdown.Application.Ports;

namespace EnhancedCountdown.Application.MidRun;

internal sealed class FrozenRuntimeRestorer
{
  private readonly IAudioTimeline audioTimeline;
  private readonly IHitSoundScheduler hitSounds;
  private readonly IFrozenVisuals visuals;
  private readonly IModLogger logger;

  internal FrozenRuntimeRestorer(
    IAudioTimeline audioTimeline,
    IHitSoundScheduler hitSounds,
    IFrozenVisuals visuals,
    IModLogger logger
  )
  {
    this.audioTimeline = audioTimeline;
    this.hitSounds = hitSounds;
    this.visuals = visuals;
    this.logger = logger;
  }

  internal void BeginPlaybackForInput(FrozenStartSession session, double elapsedSinceInput)
  {
    if (session.PlaybackStartedForInput || !audioTimeline.IsAvailable)
    {
      return;
    }
    if (!audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition, elapsedSinceInput))
    {
      return;
    }

    // The song is scheduled before the hit-sound schedule is rebuilt so its start never slips.
    if (!session.AudioSnapshot.ListenerPaused)
    {
      audioTimeline.StartPrimedSources();
    }
    hitSounds.Activate(audioTimeline.EarliestPlaybackDspTime);
    session.PlaybackStartedForInput = true;
  }

  internal void RefreezeAfterRejectedInput(FrozenStartSession session)
  {
    if (!session.PlaybackStartedForInput)
    {
      return;
    }

    audioTimeline.RefreezePrimedSources();
    hitSounds.Refreeze();
    audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition);
    visuals.RefreezePreparedEffects();
    session.PlaybackStartedForInput = false;
  }

  internal void Restore(FrozenStartSession session, bool restartAudio)
  {
    bool startPrimedSources = false;
    try
    {
      visuals.RestoreAll();
      if (session.HasAudioSnapshot && audioTimeline.IsAvailable && restartAudio && !session.PlaybackStartedForInput)
      {
        audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition);
        hitSounds.Reset();
        startPrimedSources = !session.AudioSnapshot.ListenerPaused;
      }
    }
    catch (Exception exception)
    {
      logger.LogError("Failed while restoring frozen runtime values", exception);
    }
    finally
    {
      if (session.HasAudioSnapshot)
      {
        audioTimeline.Restore(session.AudioSnapshot, startPrimedSources);
      }
    }
  }
}
