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

  internal void ReleaseAudioForInput(FrozenStartSession session)
  {
    if (session.AudioReleasedForInput || session.AudioSnapshot.ListenerPaused || !audioTimeline.IsAvailable)
    {
      return;
    }

    audioTimeline.ReleasePrimedSources();
    session.AudioReleasedForInput = true;
  }

  internal void RebaseTimelineForInput(FrozenStartSession session)
  {
    if (session.TimelineRebasedForInput || !audioTimeline.IsAvailable)
    {
      return;
    }

    if (audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition))
    {
      hitSounds.Activate();
      session.TimelineRebasedForInput = true;
    }
  }

  internal void RefreezeAfterRejectedInput(FrozenStartSession session)
  {
    if (!session.AudioReleasedForInput)
    {
      return;
    }

    audioTimeline.RefreezePrimedSources();
    if (session.TimelineRebasedForInput)
    {
      hitSounds.Refreeze();
      audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition);
    }
    visuals.RefreezePreparedEffects();
    session.AudioReleasedForInput = false;
    session.TimelineRebasedForInput = false;
  }

  internal void Restore(FrozenStartSession session, bool restartAudio)
  {
    bool unpausePrimedSources = false;
    try
    {
      visuals.RestoreAll();
      if (session.HasAudioSnapshot && audioTimeline.IsAvailable && restartAudio && !session.AudioReleasedForInput)
      {
        audioTimeline.RebaseAtFrozenTime(session.FrozenSongPosition);
        hitSounds.Reset();
        unpausePrimedSources = !session.AudioSnapshot.ListenerPaused;
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
        audioTimeline.Restore(session.AudioSnapshot, unpausePrimedSources);
      }
    }
  }
}
