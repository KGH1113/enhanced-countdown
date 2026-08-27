using System;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;
using UnityEngine;

namespace EnhancedCountdown.Infrastructure.Adofai;

internal sealed class AdofaiAudioTimeline : IAudioTimeline
{
  private scrConductor conductor;
  private float preparedAudioTime;
  private bool hasPreparedAudioTime;

  public bool IsAvailable => conductor != null;
  public double CurrentSongPosition => conductor.songposition_minusi;
  public double Crotchet => conductor.crotchetAtStart;
  public float Pitch => conductor.song.pitch;
  public double Calibration => scrConductor.calibration_i;

  public AudioRuntimeSnapshot CaptureAndFreeze()
  {
    scrConductor currentConductor = ADOBase.conductor;
    if (currentConductor == null || ADOBase.playerManager == null)
    {
      throw new InvalidOperationException("The conductor or player manager is unavailable.");
    }

    conductor = currentConductor;
    var snapshot = new AudioRuntimeSnapshot(
      Time.timeScale,
      AudioListener.pause,
      conductor.enabled,
      conductor.songposition_minusi
    );
    Time.timeScale = 0f;
    conductor.enabled = false;
    return snapshot;
  }

  public void PrimeSongSources()
  {
    if (!hasPreparedAudioTime && conductor.song != null && conductor.song.clip != null)
    {
      preparedAudioTime = conductor.song.time;
      hasPreparedAudioTime = true;
    }
    if (ADOBase.controller != null && ADOBase.controller.startVolume > 0f)
    {
      conductor.song.volume = ADOBase.controller.startVolume;
    }

    PrimeSongSource(conductor.song);
    PrimeSongSource(conductor.song2);
    PrimeSongSource(conductor.song3);
  }

  public void PauseListener()
  {
    AudioListener.pause = true;
  }

  public bool RebaseAtFrozenTime(double frozenSongPosition, double elapsedSinceFirstInput = 0.0)
  {
    if (conductor?.song == null || conductor.song.pitch == 0f)
    {
      return false;
    }

    double now = AudioSettings.dspTime;
    conductor.dspTime = now;
    conductor.prev_dspTime = now;
    conductor.dspTimeSong =
      now
      - elapsedSinceFirstInput
      - scrConductor.calibration_i
      - (frozenSongPosition + conductor.addoffset) / conductor.song.pitch;
    double resumedSongPosition = frozenSongPosition + elapsedSinceFirstInput * conductor.song.pitch;
    conductor.songposition_minusi = resumedSongPosition;
    conductor.deltaSongPos = 0.0;
    return true;
  }

  public void ReleasePrimedSources()
  {
    // PrimeSongSources already sought each clip during warmup. Rewriting AudioSource.time here
    // can synchronously seek compressed clips and make short post-start tiles immediately overdue.
    UnpauseSongSources();
    AudioListener.pause = false;
  }

  public void RefreezePrimedSources()
  {
    PrimeSongSources();
    AudioListener.pause = true;
  }

  public void Restore(AudioRuntimeSnapshot snapshot, bool unpausePrimedSources)
  {
    try
    {
      if (conductor != null)
      {
        conductor.enabled = snapshot.ConductorEnabled;
      }
      if (unpausePrimedSources && conductor != null)
      {
        UnpauseSongSources();
      }
      AudioListener.pause = snapshot.ListenerPaused;
      Time.timeScale = snapshot.TimeScale;
    }
    finally
    {
      conductor = null;
      preparedAudioTime = 0f;
      hasPreparedAudioTime = false;
    }
  }

  public void RebaseAsyncInputClock()
  {
    if (!AsyncInputManager.isActive)
    {
      return;
    }

    scrConductor activeConductor = ADOBase.conductor;
    ulong wallTickBefore = (ulong)DateTime.Now.Ticks;
    double currentDspTime = AudioSettings.dspTime;
    ulong wallTickAfter = (ulong)DateTime.Now.Ticks;
    ulong nowTick = wallTickBefore + (wallTickAfter - wallTickBefore) / 2UL;
    ulong currentDspTick = (ulong)Math.Max(0.0, currentDspTime * TimeSpan.TicksPerSecond);
    ulong newOffsetTick = nowTick >= currentDspTick ? nowTick - currentDspTick : 0UL;

    AsyncInputManager.prevFrameTick = nowTick;
    AsyncInputManager.currFrameTick = nowTick;
    AsyncInputManager.previousFrameTime = Time.unscaledTimeAsDouble;
    AsyncInputManager.offsetTick = newOffsetTick;
    AsyncInputManager.offsetTickUpdated = true;

    if (activeConductor == null || activeConductor.song == null || activeConductor.song.pitch == 0f)
    {
      return;
    }

    activeConductor.dspTime = currentDspTime;
    activeConductor.prev_dspTime = currentDspTime;
  }

  private void PrimeSongSource(AudioSource source)
  {
    if (source == null || source.clip == null)
    {
      return;
    }

    source.Play();
    source.time = Mathf.Clamp(preparedAudioTime, 0f, source.clip.length);
    source.Pause();
  }

  private void UnpauseSongSources()
  {
    conductor.song?.UnPause();
    conductor.song2?.UnPause();
    conductor.song3?.UnPause();
  }
}
