using System;
using RemoveCountdown.Application.Ports;
using RemoveCountdown.Domain.MidRun;
using UnityEngine;

namespace RemoveCountdown.Infrastructure.Unity;

internal sealed class UnityFrozenMetronome : IMetronome
{
  private const double MinimumInitialBpm = 200.0;
  private const double MaximumInitialBpm = 500.0;
  private const double SchedulingLeadSeconds = 0.05;
  private const float FallbackAccentGain = 1.35f;
  private readonly IModLogger logger;
  private UnityMetronomeControlPanel controlPanel;
  private UnityMetronomeDisplay metronomeDisplay;
  private GameObject metronomeObject;
  private AudioSource metronomeSource;
  private AudioClip metronomeLoopClip;
  private AudioSource pendingSource;
  private AudioClip pendingLoopClip;
  private MetronomePlayback playback;
  private MetronomePlayback pendingPlayback;
  private MetronomeSettings sessionSettings;
  private MetronomeSettings activeSettings;
  private MetronomeSettings pendingSettings;
  private double sessionDefaultClickBpm;
  private bool hasPlayback;
  private bool hasPendingPlayback;
  private bool hasSessionSettings;
  private bool isEnabledForSession = true;
  private bool disableRequested;

  internal UnityFrozenMetronome(IModLogger logger)
  {
    this.logger = logger;
  }

  public bool IsEnabledForSession => isEnabledForSession;

  public bool IsUiConsumingInput => controlPanel?.IsConsumingInput == true;

  public bool ConsumeDisableRequest()
  {
    if (!disableRequested)
    {
      return false;
    }
    disableRequested = false;
    return true;
  }

  public MetronomePlayback? Start()
  {
    Stop();
    if (!isEnabledForSession)
    {
      return null;
    }
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
    MetronomeSettings settings = hasSessionSettings
      ? sessionSettings
      : new MetronomeSettings(NormalizeInitialBpm(originalBpm), 4, 4);
    try
    {
      AudioClip hatClip = AudioManager.Instance.FindOrLoadAudioClip("sndHat");
      if (hatClip == null)
      {
        logger.Log("Skipped the frozen-start metronome because sndHat could not be loaded.");
        return null;
      }

      AudioClip kickClip = TryLoadKickClip();
      metronomeLoopClip = CreateLoopClip(hatClip, kickClip, settings, out int loopFrames);

      metronomeObject = new GameObject("RemoveCountdown Frozen Metronome");
      UnityEngine.Object.DontDestroyOnLoad(metronomeObject);
      metronomeSource = CreateSource(metronomeLoopClip, conductor);
      double clickInterval = (double)loopFrames / hatClip.frequency;
      double dspStartTime = AudioSettings.dspTime + SchedulingLeadSeconds;
      double startedRealtime = Time.realtimeSinceStartupAsDouble;
      metronomeSource.PlayScheduled(dspStartTime);
      playback = new MetronomePlayback(
        originalBpm,
        settings.ClickBpm,
        startedRealtime,
        dspStartTime,
        clickInterval,
        loopFrames
      );
      hasPlayback = true;
      activeSettings = settings;
      sessionSettings = settings;
      if (!hasSessionSettings)
      {
        sessionDefaultClickBpm = settings.ClickBpm;
      }
      hasSessionSettings = true;
      try
      {
        metronomeDisplay = UnityMetronomeDisplay.Create(playback);
      }
      catch (Exception exception)
      {
        logger.LogError("Failed to create the frozen-start metronome display", exception);
        metronomeDisplay = null;
      }
      try
      {
        controlPanel = UnityMetronomeControlPanel.Load(
          sessionSettings,
          sessionDefaultClickBpm,
          RequestSettings,
          RequestDisable
        );
      }
      catch (Exception exception)
      {
        logger.LogError("Failed to create the frozen-start metronome control panel", exception);
        controlPanel = null;
      }
      logger.Log(
        $"Started frozen-start metronome at {settings.ClickBpm:F1} BPM with a "
          + $"{settings.Numerator}/{settings.Denominator} accent pattern (game countdown {originalBpm:F3} BPM)."
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
    PromotePendingPlaybackIfDue();
    if (metronomeDisplay != null && metronomeSource != null)
    {
      metronomeDisplay.Update(metronomeSource.timeSamples, metronomeSource.isPlaying);
    }
  }

  public void ResetSessionSettings()
  {
    Stop();
    sessionSettings = default;
    activeSettings = default;
    pendingSettings = default;
    sessionDefaultClickBpm = 0.0;
    hasSessionSettings = false;
    isEnabledForSession = true;
    disableRequested = false;
  }

  public void Stop(string reason = null)
  {
    bool wasRunning = metronomeObject != null || metronomeDisplay != null || controlPanel != null;
    controlPanel?.Dispose();
    metronomeDisplay?.Dispose();
    metronomeSource?.Stop();
    pendingSource?.Stop();
    if (metronomeObject != null)
    {
      UnityEngine.Object.Destroy(metronomeObject);
    }
    if (metronomeLoopClip != null)
    {
      UnityEngine.Object.Destroy(metronomeLoopClip);
    }
    if (pendingLoopClip != null)
    {
      UnityEngine.Object.Destroy(pendingLoopClip);
    }

    controlPanel = null;
    metronomeDisplay = null;
    metronomeSource = null;
    metronomeObject = null;
    metronomeLoopClip = null;
    pendingSource = null;
    pendingLoopClip = null;
    playback = default;
    pendingPlayback = default;
    activeSettings = default;
    pendingSettings = default;
    hasPlayback = false;
    hasPendingPlayback = false;
    if (wasRunning && !string.IsNullOrEmpty(reason))
    {
      logger.Log($"Stopped frozen-start metronome: {reason}.");
    }
  }

  private void RequestSettings(MetronomeSettings requested)
  {
    PromotePendingPlaybackIfDue();
    MetronomeSettings comparison = hasPendingPlayback ? pendingSettings : activeSettings;
    if (!hasPlayback || metronomeSource == null || requested == comparison)
    {
      sessionSettings = requested;
      controlPanel?.SetSettings(sessionSettings);
      return;
    }

    if (requested.ClickBpm.Equals(comparison.ClickBpm) && requested.Numerator == comparison.Numerator)
    {
      sessionSettings = requested;
      if (hasPendingPlayback)
      {
        pendingSettings = requested;
      }
      controlPanel?.SetSettings(sessionSettings);
      logger.Log($"Changed metronome time signature label to {requested.Numerator}/{requested.Denominator}.");
      return;
    }

    AudioClip replacementClip = null;
    AudioSource replacementSource = null;
    try
    {
      AudioClip hatClip = AudioManager.Instance.FindOrLoadAudioClip("sndHat");
      if (hatClip == null)
      {
        throw new InvalidOperationException("sndHat could not be loaded for the metronome setting change.");
      }

      replacementClip = CreateLoopClip(hatClip, TryLoadKickClip(), requested, out int loopFrames);
      replacementSource = CreateSource(replacementClip, metronomeSource);
      double transitionTime = NextSafeTickTime(playback, AudioSettings.dspTime + SchedulingLeadSeconds);
      double clickInterval = (double)loopFrames / hatClip.frequency;
      var replacementPlayback = new MetronomePlayback(
        playback.OriginalBpm,
        requested.ClickBpm,
        Time.realtimeSinceStartupAsDouble,
        transitionTime,
        clickInterval,
        loopFrames
      );
      replacementSource.PlayScheduled(transitionTime);
      metronomeSource.SetScheduledEndTime(transitionTime);
      CancelPendingPlayback();
      pendingLoopClip = replacementClip;
      pendingSource = replacementSource;
      pendingPlayback = replacementPlayback;
      pendingSettings = requested;
      hasPendingPlayback = true;
      replacementClip = null;
      replacementSource = null;
      sessionSettings = requested;
      controlPanel?.SetSettings(sessionSettings);
      logger.Log(
        $"Scheduled metronome change to {requested.ClickBpm:F1} BPM and "
          + $"{requested.Numerator}/{requested.Denominator} at DSP {transitionTime:F6}."
      );
    }
    catch (Exception exception)
    {
      replacementSource?.Stop();
      if (replacementSource != null)
      {
        UnityEngine.Object.Destroy(replacementSource);
      }
      if (replacementClip != null)
      {
        UnityEngine.Object.Destroy(replacementClip);
      }
      controlPanel?.SetSettings(sessionSettings);
      logger.LogError("Failed to schedule the metronome setting change", exception);
    }
  }

  private void RequestDisable()
  {
    isEnabledForSession = false;
    disableRequested = true;
  }

  private void PromotePendingPlaybackIfDue()
  {
    if (!hasPendingPlayback || AudioSettings.dspTime < pendingPlayback.DspStartTime)
    {
      return;
    }

    AudioSource previousSource = metronomeSource;
    AudioClip previousClip = metronomeLoopClip;
    metronomeSource = pendingSource;
    metronomeLoopClip = pendingLoopClip;
    playback = pendingPlayback;
    activeSettings = pendingSettings;
    pendingSource = null;
    pendingLoopClip = null;
    pendingPlayback = default;
    pendingSettings = default;
    hasPendingPlayback = false;
    metronomeDisplay?.SetPlayback(playback);

    if (previousSource != null)
    {
      UnityEngine.Object.Destroy(previousSource);
    }
    if (previousClip != null)
    {
      UnityEngine.Object.Destroy(previousClip);
    }
  }

  private void CancelPendingPlayback()
  {
    pendingSource?.Stop();
    if (pendingSource != null)
    {
      UnityEngine.Object.Destroy(pendingSource);
    }
    if (pendingLoopClip != null)
    {
      UnityEngine.Object.Destroy(pendingLoopClip);
    }
    pendingSource = null;
    pendingLoopClip = null;
    pendingPlayback = default;
    pendingSettings = default;
    hasPendingPlayback = false;
  }

  private AudioClip TryLoadKickClip()
  {
    try
    {
      return AudioManager.Instance.FindOrLoadAudioClip("sndKick");
    }
    catch (Exception exception)
    {
      logger.LogError("Failed to load sndKick for the frozen-start accent", exception);
      return null;
    }
  }

  private AudioClip CreateLoopClip(
    AudioClip hatClip,
    AudioClip kickClip,
    MetronomeSettings settings,
    out int loopFrames
  )
  {
    double interval = 60.0 / settings.ClickBpm;
    loopFrames = Math.Max(1, (int)Math.Round(hatClip.frequency * interval));
    if (!TryCreateClickSamples(hatClip, hatClip.frequency, hatClip.channels, loopFrames, out float[] weakSamples))
    {
      throw new InvalidOperationException("sndHat sample data is unavailable.");
    }

    if (
      kickClip == null
      || !TryCreateClickSamples(kickClip, hatClip.frequency, hatClip.channels, loopFrames, out float[] accentSamples)
    )
    {
      accentSamples = CreateAmplifiedCopy(weakSamples, FallbackAccentGain);
      logger.Log("Used an amplified sndHat for the frozen-start accent because sndKick was unavailable.");
    }

    int samplesPerBeat = loopFrames * hatClip.channels;
    float[] loopSamples = new float[samplesPerBeat * settings.Numerator];
    Array.Copy(accentSamples, 0, loopSamples, 0, samplesPerBeat);
    for (int beatIndex = 1; beatIndex < settings.Numerator; beatIndex++)
    {
      Array.Copy(weakSamples, 0, loopSamples, beatIndex * samplesPerBeat, samplesPerBeat);
    }

    AudioClip loopClip = AudioClip.Create(
      "RemoveCountdown Frozen Metronome",
      loopFrames * settings.Numerator,
      hatClip.channels,
      hatClip.frequency,
      stream: false
    );
    if (loopClip == null || !loopClip.SetData(loopSamples, 0))
    {
      if (loopClip != null)
      {
        UnityEngine.Object.Destroy(loopClip);
      }
      throw new InvalidOperationException("The frozen-start metronome loop sample data could not be assigned.");
    }
    return loopClip;
  }

  private AudioSource CreateSource(AudioClip clip, scrConductor conductor)
  {
    AudioSource source = metronomeObject.AddComponent<AudioSource>();
    ConfigureSource(source, clip, conductor.hitSoundVolume, conductor.hitSoundGroup);
    return source;
  }

  private AudioSource CreateSource(AudioClip clip, AudioSource template)
  {
    AudioSource source = metronomeObject.AddComponent<AudioSource>();
    ConfigureSource(source, clip, template.volume, template.outputAudioMixerGroup);
    return source;
  }

  private static void ConfigureSource(
    AudioSource source,
    AudioClip clip,
    float volume,
    UnityEngine.Audio.AudioMixerGroup mixerGroup
  )
  {
    source.playOnAwake = false;
    source.loop = true;
    source.spatialBlend = 0f;
    source.pitch = 1f;
    source.priority = 10;
    source.volume = volume;
    source.outputAudioMixerGroup = mixerGroup;
    source.ignoreListenerPause = true;
    source.clip = clip;
  }

  private static double NextSafeTickTime(MetronomePlayback activePlayback, double earliestTime)
  {
    if (earliestTime <= activePlayback.DspStartTime)
    {
      return activePlayback.DspStartTime;
    }
    double elapsedTicks = (earliestTime - activePlayback.DspStartTime) / activePlayback.ClickInterval;
    return activePlayback.DspStartTime + Math.Ceiling(elapsedTicks) * activePlayback.ClickInterval;
  }

  private static double NormalizeInitialBpm(double bpm)
  {
    while (bpm < MinimumInitialBpm)
    {
      bpm *= 2.0;
    }
    while (bpm > MaximumInitialBpm)
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
