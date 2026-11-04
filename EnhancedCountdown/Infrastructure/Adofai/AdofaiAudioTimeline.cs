using System;
using System.Globalization;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;
using EnhancedCountdown.Infrastructure.Compatibility;
using UnityEngine;

namespace EnhancedCountdown.Infrastructure.Adofai;

internal sealed class AdofaiAudioTimeline : IAudioTimeline
{
  // The resumed song is started with PlayScheduled so it shares the DSP clock with the hit sounds.
  // Scheduling needs a small lead over the mixer; two DSP buffers is enough without adding much delay.
  private const int StartLeadBuffers = 2;
  private const double MinimumStartLeadSeconds = 0.008;
  private const double MaximumStartLeadSeconds = 0.06;

  // Async input reports when the key was pressed. Priming this much extra audio lets the timeline be
  // anchored at the press instead of at the frame that processed it.
  private const double AsyncPressCompensationSeconds = 1.0 / 60.0;

  // While frozen the song is parked on a far-future schedule so it can be pre-seeked and started later
  // by moving the schedule only.
  private const double ParkedStartDelaySeconds = 3600.0;

  private const double SyncProbeDelaySeconds = 0.5;
  private const int SyncProbeSampleCount = 8;

  private readonly IModLogger logger;
  private scrConductor conductor;
  private double primedAudioSongPosition;
  private bool sourcesPrimed;
  private double startLeadSeconds;
  private double maximumInputCompensationSeconds;
  private double primedStartOffsetSeconds;
  private double anchorDspTime;

  private scrConductor probeConductor;
  private double probeStartDspTime;
  private double probeOffsetSum;
  private int probeSamples;

  internal AdofaiAudioTimeline(IModLogger logger)
  {
    this.logger = logger;
  }

  public bool IsAvailable => conductor != null;
  public double CurrentSongPosition => conductor.songposition_minusi;
  public double Crotchet => conductor.crotchetAtStart;
  public float Pitch => conductor.song.pitch;
  public double Calibration => scrConductor.calibration_i;
  public double EarliestPlaybackDspTime => anchorDspTime + startLeadSeconds;

  public AudioRuntimeSnapshot CaptureAndFreeze()
  {
    scrConductor currentConductor = ADOBase.conductor;
    if (currentConductor == null || ADOBase.playerManager == null)
    {
      throw new InvalidOperationException("The conductor or player manager is unavailable.");
    }

    CancelSyncProbe();
    conductor = currentConductor;
    var snapshot = new AudioRuntimeSnapshot(
      Time.timeScale,
      AudioListener.pause,
      conductor.enabled,
      conductor.songposition_minusi
    );
    Time.timeScale = 0f;
    conductor.enabled = false;
    // The native scrub already scheduled the song; keep it silent while the world is prepared.
    AudioListener.pause = true;
    return snapshot;
  }

  public void PrimeSongSources(double audioSongPosition)
  {
    if (conductor?.song == null || conductor.song.pitch == 0f)
    {
      return;
    }

    primedAudioSongPosition = audioSongPosition;
    CalculateStartLead();
    if (ADOBase.controller != null && ADOBase.controller.startVolume > 0f)
    {
      conductor.song.volume = ADOBase.controller.startVolume;
    }
    ParkSongSources();
    sourcesPrimed = true;
  }

  public bool RebaseAtFrozenTime(double frozenSongPosition, double elapsedSinceInput = 0.0)
  {
    if (conductor?.song == null || conductor.song.pitch == 0f)
    {
      return false;
    }

    double now = AudioSettings.dspTime;
    double elapsed = Math.Min(Math.Max(0.0, elapsedSinceInput), maximumInputCompensationSeconds);
    anchorDspTime = now - elapsed;
    conductor.dspTime = now;
    conductor.prev_dspTime = now;
    conductor.dspTimeSong =
      anchorDspTime - scrConductor.calibration_i - (frozenSongPosition + conductor.addoffset) / conductor.song.pitch;
    // The accepted input is judged at the PP timestamp; the next conductor update advances from the anchor.
    conductor.songposition_minusi = frozenSongPosition;
    conductor.deltaSongPos = 0.0;
    return true;
  }

  public void StartPrimedSources()
  {
    if (StartParkedSources(out double startDspTime))
    {
      BeginSyncProbe(startDspTime);
    }
  }

  public void RefreezePrimedSources()
  {
    CancelSyncProbe();
    if (sourcesPrimed && conductor != null)
    {
      ParkSongSources();
    }
    AudioListener.pause = true;
  }

  public void Restore(AudioRuntimeSnapshot snapshot, bool startPrimedSources)
  {
    try
    {
      if (conductor != null)
      {
        conductor.enabled = snapshot.ConductorEnabled;
      }
      if (startPrimedSources)
      {
        StartParkedSources(out _);
      }
      AudioListener.pause = snapshot.ListenerPaused;
      Time.timeScale = snapshot.TimeScale;
    }
    finally
    {
      conductor = null;
      primedAudioSongPosition = 0.0;
      sourcesPrimed = false;
      startLeadSeconds = 0.0;
      maximumInputCompensationSeconds = 0.0;
      primedStartOffsetSeconds = 0.0;
      anchorDspTime = 0.0;
    }
  }

  public void RebaseAsyncInputClock()
  {
    if (!AsyncInputManager.isActive)
    {
      return;
    }

    scrConductor activeConductor = ADOBase.conductor;
    long wallTickBefore = DateTime.Now.Ticks;
    double currentDspTime = AudioSettings.dspTime;
    long wallTickAfter = DateTime.Now.Ticks;
    long nowTick = wallTickBefore + (wallTickAfter - wallTickBefore) / 2L;
    long currentDspTick = (long)Math.Max(0.0, currentDspTime * TimeSpan.TicksPerSecond);
    long newOffsetTick = nowTick >= currentDspTick ? nowTick - currentDspTick : 0L;

    AdofaiRuntimeApi.SetAsyncInputClock(nowTick, newOffsetTick);
    AsyncInputManager.previousFrameTime = Time.unscaledTimeAsDouble;
    AsyncInputManager.offsetTickUpdated = true;

    if (activeConductor == null || activeConductor.song == null || activeConductor.song.pitch == 0f)
    {
      return;
    }

    activeConductor.dspTime = currentDspTime;
    activeConductor.prev_dspTime = currentDspTime;
  }

  public void PumpDiagnostics()
  {
    if (probeConductor == null)
    {
      return;
    }

    AudioSource song = probeConductor.song;
    double now = AudioSettings.dspTime;
    if (
      song == null
      || song.clip == null
      || song.clip.frequency <= 0
      || !ReferenceEquals(probeConductor, ADOBase.conductor)
    )
    {
      CancelSyncProbe();
      return;
    }
    if (now < probeStartDspTime || !song.isPlaying || !probeConductor.enabled || AudioListener.pause)
    {
      return;
    }

    double actualClipTime = (double)song.timeSamples / song.clip.frequency;
    double expectedClipTime = (now - probeConductor.dspTimeSong) * song.pitch - CountdownClipOffset(probeConductor);
    probeOffsetSum += (actualClipTime - expectedClipTime) / song.pitch;
    probeSamples++;
    if (probeSamples < SyncProbeSampleCount)
    {
      return;
    }

    double averageOffsetMilliseconds = probeOffsetSum / probeSamples * 1000.0;
    logger.Log(
      string.Format(
        CultureInfo.InvariantCulture,
        "Start sync check: song is {0:+0.0;-0.0;0.0} ms against the hit-sound timeline.",
        averageOffsetMilliseconds
      )
    );
    CancelSyncProbe();
  }

  private void CalculateStartLead()
  {
    AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
    int sampleRate = AudioSettings.outputSampleRate;
    double bufferSeconds = bufferLength > 0 && sampleRate > 0 ? (double)bufferLength / sampleRate : 0.01;
    double schedulingLead = Math.Min(
      MaximumStartLeadSeconds,
      Math.Max(MinimumStartLeadSeconds, StartLeadBuffers * bufferSeconds)
    );
    maximumInputCompensationSeconds = AsyncInputManager.isActive ? AsyncPressCompensationSeconds : 0.0;
    startLeadSeconds = schedulingLead + maximumInputCompensationSeconds;
  }

  private void ParkSongSources()
  {
    // Clip position at the anchor. When it falls before the clip start (separate countdown time),
    // the song starts later instead of being clamped to zero early.
    double clipTimeAtAnchor = primedAudioSongPosition + conductor.addoffset - CountdownClipOffset(conductor);
    primedStartOffsetSeconds = Math.Max(startLeadSeconds, -clipTimeAtAnchor / conductor.song.pitch);
    double parkedDspTime = AudioSettings.dspTime + ParkedStartDelaySeconds;
    ParkSource(conductor.song, clipTimeAtAnchor, parkedDspTime);
    ParkSource(conductor.song2, clipTimeAtAnchor, parkedDspTime);
    ParkSource(conductor.song3, clipTimeAtAnchor, parkedDspTime);
  }

  private void ParkSource(AudioSource source, double clipTimeAtAnchor, double parkedDspTime)
  {
    if (source == null || source.clip == null)
    {
      return;
    }

    double clipTimeAtStart = clipTimeAtAnchor + primedStartOffsetSeconds * source.pitch;
    source.PlayScheduled(parkedDspTime);
    source.time = Mathf.Clamp((float)clipTimeAtStart, 0f, source.clip.length);
  }

  private bool StartParkedSources(out double startDspTime)
  {
    startDspTime = anchorDspTime + primedStartOffsetSeconds;
    if (!sourcesPrimed || conductor == null)
    {
      return false;
    }

    ScheduleStart(conductor.song, startDspTime);
    ScheduleStart(conductor.song2, startDspTime);
    ScheduleStart(conductor.song3, startDspTime);
    AudioListener.pause = false;
    return true;
  }

  private static void ScheduleStart(AudioSource source, double startDspTime)
  {
    if (source != null && source.clip != null)
    {
      source.SetScheduledStartTime(startDspTime);
    }
  }

  private static double CountdownClipOffset(scrConductor target)
  {
    // Mirrors scrConductor.ScrubMusicToTime, which maps song positions to clip time this way.
    return target.separateCountdownTime ? target.crotchetAtStart * target.countdownTicks : 0.0;
  }

  private void BeginSyncProbe(double startDspTime)
  {
    probeConductor = conductor;
    probeStartDspTime = startDspTime + SyncProbeDelaySeconds;
    probeOffsetSum = 0.0;
    probeSamples = 0;
  }

  private void CancelSyncProbe()
  {
    probeConductor = null;
    probeOffsetSum = 0.0;
    probeSamples = 0;
  }
}
