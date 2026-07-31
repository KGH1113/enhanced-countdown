using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;

namespace RemoveCountdown;

internal static class MidRunState
{
  private const float FrozenVisualLeadRadians = 0.017453292f;
  private const double MetronomeMinimumBpm = 200.0;
  private const double MetronomeMaximumBpm = 500.0;

  private enum StartPhase
  {
    Idle,
    WaitingForScrub,
    WaitingForSchedule,
    Preparing,
    Frozen,
    Releasing,
  }

  private readonly struct PendingHitSound
  {
    internal PendingHitSound(HitSound hitSound, double time, float volume)
    {
      HitSound = hitSound;
      Time = time;
      Volume = volume;
    }

    internal HitSound HitSound { get; }
    internal double Time { get; }
    internal float Volume { get; }
  }

  private static readonly FieldInfo HitSoundsDataField = AccessTools.Field(typeof(scrConductor), "hitSoundsData");

  private static StartPhase phase;
  private static scrController controller;
  private static scrConductor conductor;
  private static int frozenFrame;
  private static double frozenSongPosition;
  private static double frozenAudioSongPosition;
  private static float savedTimeScale;
  private static bool savedAudioPause;
  private static bool savedConductorEnabled;
  private static float preparedAudioTime;
  private static bool audioReleasedForInput;
  private static scrVfxPlus frozenVfx;
  private static readonly Dictionary<scrPlanet, float> frozenCosmeticAngles = new();
  private static scrPlayer pendingFrozenInputPlayer;
  private static ulong? pendingFrozenInputTick;
  private static bool metronomeRunning;
  private static GameObject metronomeObject;
  private static AudioSource metronomeSource;
  private static AudioClip metronomeLoopClip;

  internal static bool IsFrozen => phase == StartPhase.Frozen;

  internal static void OnStartRewind(scrController instance, int requestedFloor)
  {
    RestoreAndReset("restart");

    int startFloor = requestedFloor >= 0 ? requestedFloor : GCS.checkpointNum;
    if (ADOBase.isLevelEditor && startFloor > 0 && instance != null && instance == ADOBase.controller)
    {
      controller = instance;
      phase = StartPhase.WaitingForScrub;
    }
  }

  internal static bool PrepareInitialScrub(int floorNumber)
  {
    if (
      phase != StartPhase.WaitingForScrub
      || controller == null
      || !ADOBase.isLevelEditor
      || floorNumber <= 0
      || floorNumber != GCS.checkpointNum
    )
    {
      return false;
    }

    phase = StartPhase.WaitingForSchedule;
    return true;
  }

  internal static void OnMusicScheduled(scrController instance)
  {
    if (
      phase != StartPhase.WaitingForSchedule
      || instance == null
      || instance != controller
      || instance != ADOBase.controller
      || !ADOBase.isLevelEditor
      || GCS.checkpointNum <= 0
    )
    {
      return;
    }

    try
    {
      PrepareFrozenStart(instance);
    }
    catch (Exception exception)
    {
      Main.LogError("Failed to prepare the frozen middle start", exception);
      RestoreAndReset("preparation failed");
    }
  }

  internal static bool PreparePlayerUpdate(scrPlayer player, ref ulong? targetTick)
  {
    if (phase != StartPhase.Frozen)
    {
      return true;
    }

    if (!RuntimeIsValid())
    {
      RestoreAndReset("run became invalid");
      return true;
    }

    if (Time.frameCount <= frozenFrame || player == null)
    {
      return false;
    }

    if (!player.responsive && player.lockInput > 0f)
    {
      player.UnlockInput();
      Main.Log("Released the inherited player input lock after the launch frame.");
    }

    if (!player.ValidInputWasTriggered())
    {
      return false;
    }

    StopFrozenMetronome("first input accepted");

    // AsyncInputUtils.AdjustAngle assumes the conductor kept advancing between the
    // event timestamp and this frame. The frozen start deliberately did not, so
    // applying that correction would move the planet past PP immediately.
    pendingFrozenInputTick = targetTick;
    targetTick = null;
    pendingFrozenInputPlayer = player;
    scrPlanet planet = player.planetarySystem?.chosenPlanet;
    if (planet != null)
    {
      Main.Log(
        $"Accepting frozen input at angle {planet.angle:F6}, "
          + $"target {planet.targetExitAngle:F6}, delta {planet.angle - planet.targetExitAngle:F6}, "
          + $"responsive {player.responsive}."
      );
    }
    return true;
  }

  internal static void OnManualHitStarting(scrPlayer player, bool isAuto)
  {
    if (phase != StartPhase.Frozen || isAuto || player == null)
    {
      return;
    }

    RestoreFrozenPlanetVisual(player.planetarySystem?.chosenPlanet);
    if (pendingFrozenInputPlayer == player)
    {
      ReleaseAudioForInput();
    }
  }

  internal static void CompletePlayerUpdate(scrPlayer player)
  {
    if (phase != StartPhase.Frozen || player == null || pendingFrozenInputPlayer != player)
    {
      return;
    }

    if (!player.alive || player.currFloor == null || player.currFloor.nextfloor == null)
    {
      pendingFrozenInputPlayer = null;
      pendingFrozenInputTick = null;
      return;
    }

    Main.Log("The original input update did not land; retrying the same input through Hit(false).");
    if (!player.Hit(isAuto: false))
    {
      Main.Log("The fallback Hit(false) was rejected; keeping the frozen start active.");
    }
  }

  internal static void OnManualHitCompleted(scrPlayer player, bool isAuto, bool moved)
  {
    if (phase != StartPhase.Frozen || isAuto || player == null)
    {
      return;
    }

    if (!moved)
    {
      RefreezeAudioAfterRejectedInput();
      StartFrozenMetronome();
      return;
    }

    Main.Log("The first input landed naturally at the frozen Pure Perfect angle.");
    pendingFrozenInputPlayer = null;
    pendingFrozenInputTick = null;
    ReleaseFrozenStart();
  }

  internal static void PumpAsyncInput()
  {
    if (phase != StartPhase.Frozen)
    {
      return;
    }

    if (!RuntimeIsValid())
    {
      RestoreAndReset("scene or editor state changed");
      return;
    }

    if (AsyncInputManager.isActive)
    {
      controller.UpdateInput();
    }
  }

  internal static void Shutdown()
  {
    RestoreAndReset("mod shutdown");
  }

  internal static void OnPauseRequested(scrController instance)
  {
    if (phase == StartPhase.Frozen && instance != null && instance == controller && !instance.paused)
    {
      RestoreAndReset("pause requested");
    }
  }

  private static void PrepareFrozenStart(scrController instance)
  {
    phase = StartPhase.Preparing;
    savedTimeScale = Time.timeScale;
    savedAudioPause = AudioListener.pause;
    conductor = ADOBase.conductor;
    savedConductorEnabled = conductor != null && conductor.enabled;
    if (conductor == null || ADOBase.playerManager == null)
    {
      throw new InvalidOperationException("The conductor or player manager is unavailable.");
    }

    frozenSongPosition = conductor.songposition_minusi;

    Time.timeScale = 0f;
    conductor.enabled = false;

    instance.ChangeState(States.PlayerControl);
    HideCountdown(instance);

    scrPlayer primary = instance.playerOne;
    if (primary == null || primary.planetarySystem?.chosenPlanet == null)
    {
      throw new InvalidOperationException("The primary player is unavailable.");
    }

    int safetyLimit = Math.Max(1, ADOBase.lm.listFloors.Count + 1);
    while (safetyLimit-- > 0 && IsNextTileAutomatic(primary))
    {
      double automaticHitTime = CalculatePerfectSongPosition(primary.planetarySystem.chosenPlanet);
      SeekLoadedWorld(automaticHitTime, automaticHitTime, scrubVfx: false);
      AdvanceAutomaticTileForAllPlayers();
    }

    if (safetyLimit <= 0)
    {
      throw new InvalidOperationException("Automatic tile preparation exceeded the floor count.");
    }

    if (primary.currFloor == null || primary.currFloor.nextfloor == null)
    {
      Main.Log("The selected start has no following manual tile; continuing without a start freeze.");
      frozenSongPosition = conductor.songposition_minusi;
      RestoreRuntimeValues(restartAudio: true);
      ResetState();
      return;
    }

    frozenSongPosition = CalculatePerfectSongPosition(primary.planetarySystem.chosenPlanet);
    frozenAudioSongPosition = CalculateCalibratedAudioSongPosition(frozenSongPosition);
    SeekLoadedWorld(frozenSongPosition, frozenAudioSongPosition, scrubVfx: true);
    PrimeSongSourcesAtFrozenTime();
    AudioListener.pause = true;
    ApplyPreLandingVisualOffset();
    HideCountdown(instance);

    frozenFrame = Time.frameCount;
    phase = StartPhase.Frozen;
    StartFrozenMetronome();
    Main.Log(
      $"Frozen editor start at tile {primary.currFloor.seqID}, "
        + $"song time {frozenSongPosition:F6}, audio time {frozenAudioSongPosition:F6}, "
        + "with the next input at Pure Perfect."
    );
  }

  private static bool IsNextTileAutomatic(scrPlayer player)
  {
    scrFloor next = player?.currFloor?.nextfloor;
    return next != null && next.auto;
  }

  private static void AdvanceAutomaticTileForAllPlayers()
  {
    bool previousAuto = RDC.auto;
    RDC.auto = true;
    try
    {
      foreach (scrPlayer player in ADOBase.playerManager)
      {
        if (player?.currFloor?.nextfloor != null && player.currFloor.nextfloor.auto)
        {
          player.keyTimes.Clear();
          if (!player.Hit(isAuto: true))
          {
            throw new InvalidOperationException(
              $"Could not advance automatic tile {player.currFloor.nextfloor.seqID}."
            );
          }
        }
      }
    }
    finally
    {
      RDC.auto = previousAuto;
    }
  }

  private static double CalculatePerfectSongPosition(scrPlanet planet)
  {
    if (planet == null || planet.player == null || planet.planetarySystem == null || planet.planetarySystem.speed == 0f)
    {
      throw new InvalidOperationException("A valid chosen planet is required to calculate PP time.");
    }

    double direction = planet.planetarySystem.isCW ? 1.0 : -1.0;
    return planet.player.lastHit
      + (planet.targetExitAngle - planet.snappedLastAngle)
        * direction
        / Math.PI
        * conductor.crotchetAtStart
        / planet.planetarySystem.speed;
  }

  private static double CalculateCalibratedAudioSongPosition(double logicalSongPosition)
  {
    // Solve the game's non-legacy scrConductor.Update() equation for the audio
    // timeline while keeping songposition_minusi at the logical PP timestamp.
    return logicalSongPosition + (double)scrConductor.calibration_i * conductor.song.pitch;
  }

  private static void SeekLoadedWorld(double logicalSongPosition, double audioSongPosition, bool scrubVfx)
  {
    frozenSongPosition = logicalSongPosition;
    var lastHits = new Dictionary<scrPlayer, double>();
    foreach (scrPlayer player in ADOBase.playerManager)
    {
      lastHits[player] = player.lastHit;
    }

    conductor.dspTime = AudioSettings.dspTime;
    conductor.ScrubMusicToTime(audioSongPosition);
    conductor.songposition_minusi = logicalSongPosition;
    if (conductor.song != null && conductor.song.clip != null)
    {
      preparedAudioTime = conductor.song.time;
    }

    foreach (KeyValuePair<scrPlayer, double> pair in lastHits)
    {
      pair.Key.lastHit = pair.Value;
      pair.Key.planetarySystem?.chosenPlanet?.Update_RefreshAngles();
    }

    if (!scrubVfx || scrVfxPlus.instance == null)
    {
      return;
    }

    frozenVfx = scrVfxPlus.instance;
    frozenVfx.pausedTweens.Clear();
    frozenVfx.ScrubToTime((float)logicalSongPosition);
    foreach (Tween tween in frozenVfx.pausedTweens)
    {
      if (tween != null && tween.active)
      {
        tween.Pause();
      }
    }
  }

  private static void HideCountdown(scrController instance)
  {
    instance.goShown = true;
    scrCountdown countdown = UnityEngine.Object.FindAnyObjectByType<scrCountdown>();
    countdown?.CancelGo();
    scrPressToStart prompt = UnityEngine.Object.FindAnyObjectByType<scrPressToStart>();
    prompt?.HideText();
  }

  private static void StartFrozenMetronome()
  {
    StopFrozenMetronome(reason: null);
    if (phase != StartPhase.Frozen || conductor == null)
    {
      return;
    }

    double originalInterval = Math.Abs(conductor.GetCountdownTime(1) - conductor.GetCountdownTime(0));
    if (originalInterval <= 0.0 || double.IsNaN(originalInterval) || double.IsInfinity(originalInterval))
    {
      Main.Log("Skipped the frozen-start metronome because the game returned an invalid countdown interval.");
      return;
    }

    double originalBpm = 60.0 / originalInterval;
    double normalizedBpm = originalBpm;
    while (normalizedBpm < MetronomeMinimumBpm)
    {
      normalizedBpm *= 2.0;
    }
    while (normalizedBpm > MetronomeMaximumBpm)
    {
      normalizedBpm *= 0.5;
    }

    try
    {
      AudioClip hatClip = AudioManager.Instance.FindOrLoadAudioClip("sndHat");
      if (hatClip == null)
      {
        Main.Log("Skipped the frozen-start metronome because sndHat could not be loaded.");
        return;
      }

      double interval = 60.0 / normalizedBpm;
      int loopFrames = Math.Max(1, (int)Math.Round(hatClip.frequency * interval));
      float[] hatSamples = new float[hatClip.samples * hatClip.channels];
      if (!hatClip.GetData(hatSamples, 0))
      {
        Main.Log("Skipped the frozen-start metronome because sndHat sample data is unavailable.");
        return;
      }

      float[] loopSamples = new float[loopFrames * hatClip.channels];
      Array.Copy(hatSamples, loopSamples, Math.Min(hatSamples.Length, loopSamples.Length));
      metronomeLoopClip = AudioClip.Create(
        "RemoveCountdown Frozen Metronome",
        loopFrames,
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
      metronomeSource.Play();
      metronomeRunning = true;
      Main.Log(
        $"Started frozen-start metronome at {normalizedBpm:F3} BPM " + $"(game countdown {originalBpm:F3} BPM)."
      );
    }
    catch (Exception exception)
    {
      Main.LogError("Failed to start the frozen-start metronome", exception);
      StopFrozenMetronome("startup failed");
    }
  }

  private static void StopFrozenMetronome(string reason)
  {
    bool wasRunning = metronomeRunning || metronomeObject != null;
    metronomeRunning = false;
    if (metronomeSource != null)
    {
      metronomeSource.Stop();
    }
    if (metronomeObject != null)
    {
      UnityEngine.Object.Destroy(metronomeObject);
    }
    if (metronomeLoopClip != null)
    {
      UnityEngine.Object.Destroy(metronomeLoopClip);
    }
    metronomeSource = null;
    metronomeObject = null;
    metronomeLoopClip = null;

    if (wasRunning && !string.IsNullOrEmpty(reason))
    {
      Main.Log($"Stopped frozen-start metronome: {reason}.");
    }
  }

  private static void ApplyPreLandingVisualOffset()
  {
    frozenCosmeticAngles.Clear();
    foreach (scrPlayer player in ADOBase.playerManager)
    {
      scrPlanet planet = player?.planetarySystem?.chosenPlanet;
      if (planet == null)
      {
        continue;
      }

      frozenCosmeticAngles[planet] = planet.cosmeticAngle;
      float direction = planet.planetarySystem.isCW ? -1f : 1f;
      planet.cosmeticAngle += FrozenVisualLeadRadians * direction;
      planet.Update_RefreshAngles();
    }
  }

  private static void RestoreFrozenPlanetVisual(scrPlanet planet)
  {
    if (planet == null || !frozenCosmeticAngles.TryGetValue(planet, out float originalAngle))
    {
      return;
    }

    planet.cosmeticAngle = originalAngle;
    frozenCosmeticAngles.Remove(planet);
    planet.Update_RefreshAngles();
  }

  private static void RestoreAllFrozenPlanetVisuals()
  {
    foreach (KeyValuePair<scrPlanet, float> pair in frozenCosmeticAngles)
    {
      if (pair.Key != null)
      {
        pair.Key.cosmeticAngle = pair.Value;
        pair.Key.Update_RefreshAngles();
      }
    }
    frozenCosmeticAngles.Clear();
  }

  private static void ReleaseFrozenStart()
  {
    if (phase != StartPhase.Frozen)
    {
      return;
    }

    phase = StartPhase.Releasing;
    RestoreRuntimeValues(restartAudio: true);
    RebaseAsyncInputClock();
    Main.Log("Resumed the loaded run from the frozen Pure Perfect timestamp.");
    ResetState();
  }

  private static void RebaseAsyncInputClock()
  {
    if (!AsyncInputManager.isActive)
    {
      return;
    }

    ulong nowTick = (ulong)DateTime.Now.Ticks;
    AsyncInputManager.prevFrameTick = nowTick;
    AsyncInputManager.currFrameTick = nowTick;
    AsyncInputManager.previousFrameTime = Time.unscaledTimeAsDouble;
    AsyncInputManager.offsetTickUpdated = false;
    AsyncInputUtils.UpdateOffsetTime(1L);
    Main.Log("Rebased the async input clock to the resumed conductor timeline.");
  }

  private static void RestoreAndReset(string reason)
  {
    StopFrozenMetronome(reason);
    if (phase == StartPhase.Frozen || phase == StartPhase.Preparing || phase == StartPhase.Releasing)
    {
      RestoreRuntimeValues(restartAudio: phase == StartPhase.Frozen || phase == StartPhase.Preparing);
      Main.Log($"Cleared frozen start state: {reason}.");
    }

    ResetState();
  }

  private static void RestoreRuntimeValues(bool restartAudio)
  {
    bool shouldLogSongSources = false;
    bool shouldUnpausePrimedSources = false;
    try
    {
      RestoreAllFrozenPlanetVisuals();
      if (frozenVfx != null)
      {
        foreach (Tween tween in frozenVfx.pausedTweens)
        {
          if (tween != null && tween.active)
          {
            tween.Play();
          }
        }
        frozenVfx.pausedTweens.Clear();
      }

      if (conductor != null && restartAudio && !audioReleasedForInput)
      {
        RebaseAudioAtFrozenTime();
        shouldUnpausePrimedSources = !savedAudioPause;
      }
      shouldLogSongSources = conductor != null && restartAudio && !savedAudioPause;
    }
    catch (Exception exception)
    {
      Main.LogError("Failed while restoring frozen runtime values", exception);
    }
    finally
    {
      if (conductor != null)
      {
        conductor.enabled = savedConductorEnabled;
      }
      if (shouldUnpausePrimedSources && conductor != null)
      {
        UnpauseSongSources();
      }
      AudioListener.pause = savedAudioPause;
      Time.timeScale = savedTimeScale;
      if (shouldLogSongSources && conductor != null)
      {
        LogSongSources();
      }
    }
  }

  private static void RebaseAudioAtFrozenTime(double elapsedSinceFirstInput = 0.0)
  {
    if (conductor.song == null || conductor.song.pitch == 0f)
    {
      return;
    }

    double now = AudioSettings.dspTime;
    double resumedSongPosition = frozenSongPosition + elapsedSinceFirstInput * conductor.song.pitch;
    conductor.dspTime = now;
    conductor.prev_dspTime = now;
    // The AudioSource was scrubbed to frozenAudioSongPosition before the wait.
    // Anchor the game's original Update() equation at the logical PP timestamp,
    // so releasing the first hit cannot introduce a calibration-sized jump.
    conductor.dspTimeSong =
      now
      - elapsedSinceFirstInput
      - scrConductor.calibration_i
      - (frozenSongPosition + conductor.addoffset) / conductor.song.pitch;
    conductor.songposition_minusi = resumedSongPosition;
    conductor.deltaSongPos = 0.0;

    AudioManager.Instance.StopAllSounds();
    RebuildHitScheduleFromCheckpoint();
  }

  private static void RebuildHitScheduleFromCheckpoint()
  {
    // PlayHitTimes normally drops every sound whose lead-in time is not later
    // than conductor.dspTime. A frozen start rebuilds the schedule at the first
    // tile's PP instant, so that rule would discard the first hit sound (and
    // nearby sounds on very fast patterns) before the conductor can schedule
    // them. Build the complete schedule once to capture that first sound, then
    // rebuild at the real DSP time so already elapsed countdown ticks stay out.
    double currentDspTime = conductor.dspTime;
    PendingHitSound? missedHitSound = null;
    try
    {
      conductor.dspTime = double.NegativeInfinity;
      conductor.PlayHitTimes();
      missedHitSound = CaptureFirstMissedHitSound(currentDspTime);

      // Rebuild once more at the real time. In particular, this discards old
      // countdown ticks that would otherwise all play at once on the next frame.
      conductor.dspTime = currentDspTime;
      conductor.PlayHitTimes();
    }
    finally
    {
      conductor.dspTime = currentDspTime;
    }

    if (missedHitSound is PendingHitSound hitSound)
    {
      double playbackTime = Math.Max(currentDspTime, AudioSettings.dspTime);
      AudioManager.Play("snd" + hitSound.HitSound, playbackTime, conductor.hitSoundGroup, hitSound.Volume);
      Main.Log(
        $"Restored frozen-start hit sound {hitSound.HitSound} "
          + $"{playbackTime - hitSound.Time:F6}s after its scheduled time."
      );
    }
  }

  private static PendingHitSound? CaptureFirstMissedHitSound(double currentDspTime)
  {
    if (HitSoundsDataField?.GetValue(conductor) is not IEnumerable hitSoundsData)
    {
      return null;
    }

    foreach (object item in hitSoundsData)
    {
      if (item == null)
      {
        continue;
      }

      Type itemType = item.GetType();
      FieldInfo hitSoundField = AccessTools.Field(itemType, "hitSound");
      FieldInfo timeField = AccessTools.Field(itemType, "time");
      FieldInfo volumeField = AccessTools.Field(itemType, "volume");
      if (hitSoundField == null || timeField == null || volumeField == null)
      {
        continue;
      }

      double time = (double)timeField.GetValue(item);
      if (time > currentDspTime)
      {
        return null;
      }

      return new PendingHitSound((HitSound)hitSoundField.GetValue(item), time, (float)volumeField.GetValue(item));
    }

    return null;
  }

  private static void PrimeSongSourcesAtFrozenTime()
  {
    if (controller != null && controller.startVolume > 0f)
    {
      conductor.song.volume = controller.startVolume;
    }

    PrimeSongSource(conductor.song);
    PrimeSongSource(conductor.song2);
    PrimeSongSource(conductor.song3);
  }

  private static void PrimeSongSource(AudioSource source)
  {
    if (source == null || source.clip == null)
    {
      return;
    }

    float resumeTime = Mathf.Clamp(preparedAudioTime, 0f, source.clip.length);
    // Create the audio voice before the global listener is paused, then park it
    // at the PP sample. The first input can resume this existing voice without
    // paying the startup cost of Play or PlayScheduled.
    source.Play();
    source.time = resumeTime;
    source.Pause();
  }

  private static void ReleaseAudioForInput()
  {
    if (audioReleasedForInput || savedAudioPause || conductor == null)
    {
      return;
    }

    double elapsedSinceFirstInput = GetPendingInputElapsedSeconds();
    RebaseAudioAtFrozenTime(elapsedSinceFirstInput);
    AdvancePrimedSongSources(elapsedSinceFirstInput);
    UnpauseSongSources();
    AudioListener.pause = false;
    audioReleasedForInput = true;
    Main.Log(
      $"Released the primed audio on the first input before Hit(false), "
        + $"preserving {elapsedSinceFirstInput * 1000.0:F3} ms of async event time."
    );
  }

  private static double GetPendingInputElapsedSeconds()
  {
    if (!pendingFrozenInputTick.HasValue || !AsyncInputManager.isActive)
    {
      return 0.0;
    }

    ulong currentTick = (ulong)DateTime.Now.Ticks;
    ulong inputTick = pendingFrozenInputTick.Value;
    return currentTick > inputTick ? (currentTick - inputTick) / 10000000.0 : 0.0;
  }

  private static void AdvancePrimedSongSources(double elapsedSinceFirstInput)
  {
    if (elapsedSinceFirstInput <= 0.0)
    {
      return;
    }

    AdvancePrimedSongSource(conductor.song, elapsedSinceFirstInput);
    AdvancePrimedSongSource(conductor.song2, elapsedSinceFirstInput);
    AdvancePrimedSongSource(conductor.song3, elapsedSinceFirstInput);
  }

  private static void AdvancePrimedSongSource(AudioSource source, double elapsedSinceFirstInput)
  {
    if (source == null || source.clip == null)
    {
      return;
    }

    float resumedTime = preparedAudioTime + (float)(elapsedSinceFirstInput * source.pitch);
    source.time = Mathf.Clamp(resumedTime, 0f, source.clip.length);
  }

  private static void RefreezeAudioAfterRejectedInput()
  {
    if (!audioReleasedForInput || conductor == null)
    {
      return;
    }

    PrimeSongSourcesAtFrozenTime();
    AudioListener.pause = true;
    audioReleasedForInput = false;
  }

  private static void UnpauseSongSources()
  {
    conductor.song?.UnPause();
    conductor.song2?.UnPause();
    conductor.song3?.UnPause();
  }

  private static void LogSongSources()
  {
    Main.Log(
      $"Resumed song sources: mainPlaying={conductor.song.isPlaying}, "
        + $"time={conductor.song.time:F3}, volume={conductor.song.volume:F3}, "
        + $"listenerPaused={AudioListener.pause}."
    );
  }

  private static bool RuntimeIsValid()
  {
    return ADOBase.isLevelEditor
      && GCS.checkpointNum > 0
      && controller != null
      && controller == ADOBase.controller
      && conductor != null;
  }

  private static void ResetState()
  {
    StopFrozenMetronome(reason: null);
    phase = StartPhase.Idle;
    controller = null;
    conductor = null;
    frozenVfx = null;
    frozenCosmeticAngles.Clear();
    pendingFrozenInputPlayer = null;
    pendingFrozenInputTick = null;
    preparedAudioTime = 0f;
    audioReleasedForInput = false;
    frozenFrame = 0;
    frozenSongPosition = 0.0;
    frozenAudioSongPosition = 0.0;
  }
}
