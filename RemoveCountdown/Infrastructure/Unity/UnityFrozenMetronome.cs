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
  private const int BeatsPerLoop = 4;
  private const float FallbackAccentGain = 1.35f;
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
      if (!TryCreateClickSamples(hatClip, hatClip.frequency, hatClip.channels, loopFrames, out float[] weakSamples))
      {
        logger.Log("Skipped the frozen-start metronome because sndHat sample data is unavailable.");
        return null;
      }

      float[] accentSamples;
      AudioClip kickClip = null;
      try
      {
        kickClip = AudioManager.Instance.FindOrLoadAudioClip("sndKick");
      }
      catch (Exception exception)
      {
        logger.LogError("Failed to load sndKick for the frozen-start accent", exception);
      }
      if (
        kickClip == null
        || !TryCreateClickSamples(kickClip, hatClip.frequency, hatClip.channels, loopFrames, out accentSamples)
      )
      {
        accentSamples = CreateAmplifiedCopy(weakSamples, FallbackAccentGain);
        logger.Log("Used an amplified sndHat for the frozen-start accent because sndKick was unavailable.");
      }

      int samplesPerBeat = loopFrames * hatClip.channels;
      float[] loopSamples = new float[samplesPerBeat * BeatsPerLoop];
      Array.Copy(accentSamples, 0, loopSamples, 0, samplesPerBeat);
      for (int beatIndex = 1; beatIndex < BeatsPerLoop; beatIndex++)
      {
        Array.Copy(weakSamples, 0, loopSamples, beatIndex * samplesPerBeat, samplesPerBeat);
      }
      metronomeLoopClip = AudioClip.Create(
        "RemoveCountdown Frozen Metronome",
        loopFrames * BeatsPerLoop,
        hatClip.channels,
        hatClip.frequency,
        stream: false
      );
      if (!metronomeLoopClip.SetData(loopSamples, 0))
      {
        throw new InvalidOperationException("The frozen-start metronome loop sample data could not be assigned.");
      }

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
        $"Started frozen-start metronome at {normalizedBpm:F3} BPM with a Kick-Hat-Hat-Hat pattern "
          + $"(game countdown {originalBpm:F3} BPM)."
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

  private static bool TryCreateClickSamples(
    AudioClip sourceClip,
    int targetFrequency,
    int targetChannels,
    int targetFrames,
    out float[] targetSamples
  )
  {
    targetSamples = null;
    if (
      sourceClip == null
      || sourceClip.samples <= 0
      || sourceClip.channels <= 0
      || sourceClip.frequency <= 0
      || targetFrequency <= 0
      || targetChannels <= 0
      || targetFrames <= 0
    )
    {
      return false;
    }

    try
    {
      int sourceChannels = sourceClip.channels;
      int sourceFrames = sourceClip.samples;
      float[] sourceSamples = new float[sourceFrames * sourceChannels];
      if (!sourceClip.GetData(sourceSamples, 0))
      {
        return false;
      }

      targetSamples = ConvertSamples(
        sourceSamples,
        sourceFrames,
        sourceChannels,
        sourceClip.frequency,
        targetFrames,
        targetChannels,
        targetFrequency
      );
      return true;
    }
    catch
    {
      targetSamples = null;
      return false;
    }
  }

  private static float[] ConvertSamples(
    float[] sourceSamples,
    int sourceFrames,
    int sourceChannels,
    int sourceFrequency,
    int targetFrames,
    int targetChannels,
    int targetFrequency
  )
  {
    var targetSamples = new float[targetFrames * targetChannels];
    double sourceFramesPerTargetFrame = (double)sourceFrequency / targetFrequency;
    for (int targetFrame = 0; targetFrame < targetFrames; targetFrame++)
    {
      double sourcePosition = targetFrame * sourceFramesPerTargetFrame;
      int lowerSourceFrame = (int)sourcePosition;
      if (lowerSourceFrame >= sourceFrames)
      {
        break;
      }

      int upperSourceFrame = Math.Min(lowerSourceFrame + 1, sourceFrames - 1);
      float interpolation = (float)(sourcePosition - lowerSourceFrame);
      for (int targetChannel = 0; targetChannel < targetChannels; targetChannel++)
      {
        float lowerSample = ReadChannelSample(
          sourceSamples,
          lowerSourceFrame,
          sourceChannels,
          targetChannel,
          targetChannels
        );
        float upperSample = ReadChannelSample(
          sourceSamples,
          upperSourceFrame,
          sourceChannels,
          targetChannel,
          targetChannels
        );
        targetSamples[targetFrame * targetChannels + targetChannel] =
          lowerSample + (upperSample - lowerSample) * interpolation;
      }
    }
    return targetSamples;
  }

  private static float ReadChannelSample(
    float[] samples,
    int frame,
    int sourceChannels,
    int targetChannel,
    int targetChannels
  )
  {
    int frameOffset = frame * sourceChannels;
    if (targetChannels == 1 && sourceChannels > 1)
    {
      float sum = 0f;
      for (int sourceChannel = 0; sourceChannel < sourceChannels; sourceChannel++)
      {
        sum += samples[frameOffset + sourceChannel];
      }
      return sum / sourceChannels;
    }

    int sourceChannelIndex = sourceChannels == 1 ? 0 : Math.Min(targetChannel, sourceChannels - 1);
    return samples[frameOffset + sourceChannelIndex];
  }

  private static float[] CreateAmplifiedCopy(float[] samples, float gain)
  {
    var amplified = new float[samples.Length];
    for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
    {
      amplified[sampleIndex] = Mathf.Clamp(samples[sampleIndex] * gain, -1f, 1f);
    }
    return amplified;
  }
}
