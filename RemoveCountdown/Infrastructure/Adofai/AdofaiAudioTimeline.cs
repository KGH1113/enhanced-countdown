using System;
using RemoveCountdown.Application.Ports;
using RemoveCountdown.Domain.MidRun;
using UnityEngine;

namespace RemoveCountdown.Infrastructure.Adofai;

internal sealed class AdofaiAudioTimeline : IAudioTimeline
{
  private readonly IModLogger logger;
  private scrConductor conductor;
  private float preparedAudioTime;
  private bool hasPreparedAudioTime;

  internal AdofaiAudioTimeline(IModLogger logger)
  {
    this.logger = logger;
  }

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

  public double GetInputElapsedSeconds(ulong? inputTick)
  {
    if (!inputTick.HasValue || !AsyncInputManager.isActive)
    {
      return 0.0;
    }

    ulong currentTick = (ulong)DateTime.Now.Ticks;
    return currentTick > inputTick.Value ? (currentTick - inputTick.Value) / 10000000.0 : 0.0;
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
    logger.Log(
      $"Rebased frozen audio timeline: frozenSong={frozenSongPosition:F6}, "
        + $"inputElapsedMs={elapsedSinceFirstInput * 1000.0:F3}, resumedSong={resumedSongPosition:F6}, "
        + $"dsp={now:F6}, dspTimeSong={conductor.dspTimeSong:F6}."
    );
    return true;
  }

  public void AdvanceAndReleasePrimedSources(double elapsedSinceFirstInput)
  {
    if (elapsedSinceFirstInput > 0.0)
    {
      AdvancePrimedSongSource(conductor.song, elapsedSinceFirstInput);
      AdvancePrimedSongSource(conductor.song2, elapsedSinceFirstInput);
      AdvancePrimedSongSource(conductor.song3, elapsedSinceFirstInput);
    }

    UnpauseSongSources();
    AudioListener.pause = false;
  }

  public void RefreezePrimedSources()
  {
    PrimeSongSources();
    AudioListener.pause = true;
  }

  public void Restore(AudioRuntimeSnapshot snapshot, bool unpausePrimedSources, bool logSongSources)
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
      if (logSongSources && conductor?.song != null)
      {
        logger.Log(
          $"Resumed song sources: mainPlaying={conductor.song.isPlaying}, "
            + $"time={conductor.song.time:F3}, volume={conductor.song.volume:F3}, "
            + $"listenerPaused={AudioListener.pause}."
        );
      }
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
    double staleConductorDsp = activeConductor?.dspTime ?? double.NaN;
    ulong previousOffsetTick = AsyncInputManager.offsetTick;

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
      logger.Log(
        $"Rebased async input clock without an active song: wallTick={nowTick}, dsp={currentDspTime:F6}, "
          + $"offsetDeltaMs={((double)newOffsetTick - previousOffsetTick) / TimeSpan.TicksPerMillisecond:F3}."
      );
      return;
    }

    double pitch = activeConductor.song.pitch;
    double expectedSongPosition =
      (currentDspTime - activeConductor.dspTimeSong - scrConductor.calibration_i) * pitch - activeConductor.addoffset;
    double currentSongPosition = activeConductor.songposition_minusi;
    double mappedDspTime = (nowTick - newOffsetTick) / (double)TimeSpan.TicksPerSecond;
    double mappedSongPosition =
      (mappedDspTime - activeConductor.dspTimeSong - scrConductor.calibration_i) * pitch - activeConductor.addoffset;
    double staleDspMilliseconds = (currentDspTime - staleConductorDsp) * 1000.0;
    double offsetDeltaMilliseconds = ((double)newOffsetTick - previousOffsetTick) / TimeSpan.TicksPerMillisecond;
    double storedSongErrorMilliseconds = (currentSongPosition - expectedSongPosition) / pitch * 1000.0;
    double mappedSongErrorMilliseconds = (mappedSongPosition - expectedSongPosition) / pitch * 1000.0;

    activeConductor.dspTime = currentDspTime;
    activeConductor.prev_dspTime = currentDspTime;
    logger.Log(
      $"Rebased async input clock: staleDspMs={staleDspMilliseconds:F3}, "
        + $"offsetDeltaMs={offsetDeltaMilliseconds:F3}, wallTick={nowTick}, dsp={currentDspTime:F6}, "
        + $"currentSong={currentSongPosition:F6}, expectedSong={expectedSongPosition:F6}, "
        + $"storedSongErrorMs={storedSongErrorMilliseconds:F3}, mappedSongErrorMs={mappedSongErrorMilliseconds:F3}."
    );
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

  private void AdvancePrimedSongSource(AudioSource source, double elapsedSinceFirstInput)
  {
    if (source == null || source.clip == null)
    {
      return;
    }

    float resumedTime = preparedAudioTime + (float)(elapsedSinceFirstInput * source.pitch);
    source.time = Mathf.Clamp(resumedTime, 0f, source.clip.length);
  }

  private void UnpauseSongSources()
  {
    conductor.song?.UnPause();
    conductor.song2?.UnPause();
    conductor.song3?.UnPause();
  }
}
