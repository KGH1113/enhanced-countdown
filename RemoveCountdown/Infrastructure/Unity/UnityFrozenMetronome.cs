using System;
using RemoveCountdown.Application.Ports;
using RemoveCountdown.Domain.MidRun;
using UnityEngine;

namespace RemoveCountdown.Infrastructure.Unity;

internal sealed class UnityFrozenMetronome : IMetronome
{
  private const double MinimumBpm = 200.0;
  private const double MaximumBpm = 500.0;
  private const double SchedulingLeadSeconds = 0.05;
  private readonly IModLogger logger;
  private UnityMetronomeDisplay metronomeDisplay;
  private GameObject metronomeObject;
  private AudioSource metronomeSource;
  private AudioClip metronomeLoopClip;

  internal UnityFrozenMetronome(IModLogger logger)
  {
    this.logger = logger;
  }

  public MetronomePlayback? Start()
  {
    Stop();
    scrConductor conductor = ADOBase.conductor;
    if (conductor == null)
    {
      return null;
    }

    double originalInterval = Math.Abs(conductor.GetCountdownTime(1) - conductor.GetCountdownTime(0));
    if (originalInterval <= 0.0 || double.IsNaN(originalInterval) || double.IsInfinity(originalInterval))
    {
      logger.Log("Skipped the frozen-start metronome because the game returned an invalid countdown interval.");
      return null;
    }

    double originalBpm = 60.0 / originalInterval;
    double normalizedBpm = NormalizeBpm(originalBpm);
    try
    {
      AudioClip hatClip = AudioManager.Instance.FindOrLoadAudioClip("sndHat");
      if (hatClip == null)
      {
        logger.Log("Skipped the frozen-start metronome because sndHat could not be loaded.");
        return null;
      }

      double interval = 60.0 / normalizedBpm;
      int loopFrames = Math.Max(1, (int)Math.Round(hatClip.frequency * interval));
      float[] hatSamples = new float[hatClip.samples * hatClip.channels];
      if (!hatClip.GetData(hatSamples, 0))
      {
        logger.Log("Skipped the frozen-start metronome because sndHat sample data is unavailable.");
        return null;
      }

      int clickSampleCount = Math.Min(hatSamples.Length, loopFrames * hatClip.channels);
      float[] loopSamples = new float[loopFrames * 2 * hatClip.channels];
      Array.Copy(hatSamples, 0, loopSamples, 0, clickSampleCount);
      Array.Copy(hatSamples, 0, loopSamples, loopFrames * hatClip.channels, clickSampleCount);
      metronomeLoopClip = AudioClip.Create(
        "RemoveCountdown Frozen Metronome",
        loopFrames * 2,
        hatClip.channels,
        hatClip.frequency,
        stream: false
      );
      metronomeLoopClip.SetData(loopSamples, 0);

      metronomeObject = new GameObject("RemoveCountdown Frozen Metronome");
      UnityEngine.Object.DontDestroyOnLoad(metronomeObject);
      metronomeSource = metronomeObject.AddComponent<AudioSource>();
      metronomeSource.playOnAwake = false;
      metronomeSource.loop = true;
      metronomeSource.spatialBlend = 0f;
      metronomeSource.pitch = 1f;
      metronomeSource.priority = 10;
      metronomeSource.volume = conductor.hitSoundVolume;
      metronomeSource.outputAudioMixerGroup = conductor.hitSoundGroup;
      metronomeSource.ignoreListenerPause = true;
      metronomeSource.clip = metronomeLoopClip;
      double clickInterval = (double)loopFrames / hatClip.frequency;
      double dspStartTime = AudioSettings.dspTime + SchedulingLeadSeconds;
      double startedRealtime = Time.realtimeSinceStartupAsDouble;
      metronomeSource.PlayScheduled(dspStartTime);
      var playback = new MetronomePlayback(
        originalBpm,
        normalizedBpm,
        startedRealtime,
        dspStartTime,
        clickInterval,
        loopFrames
      );
      try
      {
        metronomeDisplay = UnityMetronomeDisplay.Create(playback);
      }
      catch (Exception exception)
      {
        logger.LogError("Failed to create the frozen-start metronome display", exception);
        metronomeDisplay = null;
      }
      logger.Log(
        $"Started frozen-start metronome at {normalizedBpm:F3} BPM " + $"(game countdown {originalBpm:F3} BPM)."
      );
      return playback;
    }
    catch (Exception exception)
    {
      logger.LogError("Failed to start the frozen-start metronome", exception);
      Stop("startup failed");
      return null;
    }
  }

  public void UpdateDisplay()
  {
    if (metronomeDisplay != null && metronomeSource != null)
    {
      metronomeDisplay.Update(metronomeSource.timeSamples, metronomeSource.isPlaying);
    }
  }

  public void Stop(string reason = null)
  {
    bool wasRunning = metronomeObject != null || metronomeDisplay != null;
    metronomeDisplay?.Dispose();
    metronomeSource?.Stop();
    if (metronomeObject != null)
    {
      UnityEngine.Object.Destroy(metronomeObject);
    }
    if (metronomeLoopClip != null)
    {
      UnityEngine.Object.Destroy(metronomeLoopClip);
    }

    metronomeDisplay = null;
    metronomeSource = null;
    metronomeObject = null;
    metronomeLoopClip = null;
    if (wasRunning && !string.IsNullOrEmpty(reason))
    {
      logger.Log($"Stopped frozen-start metronome: {reason}.");
    }
  }

  private static double NormalizeBpm(double bpm)
  {
    while (bpm < MinimumBpm)
    {
      bpm *= 2.0;
    }
    while (bpm > MaximumBpm)
    {
      bpm *= 0.5;
    }
    return bpm;
  }
}
