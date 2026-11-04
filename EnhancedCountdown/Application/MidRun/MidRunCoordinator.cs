using System;
using System.Globalization;
using EnhancedCountdown.Application.Ports;
using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.MidRun;

internal sealed partial class MidRunCoordinator
{
  private const int MinimumWarmupFrames = 2;
  private const int MaximumWarmupFrames = 5;
  private const double WarmupFrameDurationTolerance = 0.25;

  private readonly IGameWorld gameWorld;
  private readonly IAudioTimeline audioTimeline;
  private readonly IHitSoundScheduler hitSounds;
  private readonly IMetronome metronome;
  private readonly IFrozenVisuals visuals;
  private readonly IModLogger logger;
  private readonly FrozenStartPreparer startPreparer;
  private readonly FrozenRuntimeRestorer runtimeRestorer;
  private FrozenStartSession session;
  private bool restartingWithNativeCountdown;

  internal MidRunCoordinator(
    IGameWorld gameWorld,
    IAudioTimeline audioTimeline,
    IHitSoundScheduler hitSounds,
    IMetronome metronome,
    IFrozenVisuals visuals,
    IModLogger logger,
    FrozenStartPreparer startPreparer,
    FrozenRuntimeRestorer runtimeRestorer
  )
  {
    this.gameWorld = gameWorld;
    this.audioTimeline = audioTimeline;
    this.hitSounds = hitSounds;
    this.metronome = metronome;
    this.visuals = visuals;
    this.logger = logger;
    this.startPreparer = startPreparer;
    this.runtimeRestorer = runtimeRestorer;
  }

  internal bool IsFrozen => session?.Phase == FrozenStartPhase.Frozen;
  internal bool OwnsAudioTimeline =>
    session?.HasAudioSnapshot == true
    && session.Phase
      is FrozenStartPhase.Preparing
        or FrozenStartPhase.Warming
        or FrozenStartPhase.Frozen
        or FrozenStartPhase.Releasing;
  private bool IsWarming => session?.Phase == FrozenStartPhase.Warming;

  internal void OnStartRewind(scrController controller, int requestedFloor)
  {
    RestoreAndReset("restart");
    if (!metronome.IsEnabledForSession)
    {
      return;
    }
    string fallbackReason = gameWorld.GetNativeCountdownFallbackReason();
    if (fallbackReason != null)
    {
      return;
    }
    fallbackReason = hitSounds.GetCompatibilityFailureReason();
    if (fallbackReason != null)
    {
      return;
    }
    int startFloor = gameWorld.ResolveStartFloor(requestedFloor);
    if (gameWorld.CanArm(controller, startFloor))
    {
      session = new FrozenStartSession(controller);
    }
  }

  internal bool PrepareInitialScrub(int floorNumber)
  {
    if (
      session?.Phase != FrozenStartPhase.WaitingForScrub
      || !gameWorld.CanPrepareInitialScrub(session.Controller, floorNumber)
    )
    {
      return false;
    }

    session.Phase = FrozenStartPhase.WaitingForSchedule;
    return true;
  }

  internal void OnMusicScheduled(scrController controller)
  {
    if (
      session?.Phase != FrozenStartPhase.WaitingForSchedule
      || controller != session.Controller
      || !gameWorld.CanHandleMusicScheduled(controller)
    )
    {
      return;
    }

    try
    {
      session.Timings.MusicScheduledMilliseconds = session.StartupWatch.Elapsed.TotalMilliseconds;
      if (!startPreparer.Prepare(session))
      {
        runtimeRestorer.Restore(session, restartAudio: true);
        ResetSession();
      }
    }
    catch (Exception exception)
    {
      logger.LogError("Failed to prepare the frozen middle start", exception);
      RestoreAndReset("preparation failed");
    }
  }

  internal bool PreparePlayerUpdate(scrPlayer player, object inputTick, out bool clearTargetTick)
  {
    clearTargetTick = false;
    if (IsWarming)
    {
      return false;
    }
    if (!IsFrozen)
    {
      return true;
    }
    if (!gameWorld.IsRuntimeValid(session.Controller))
    {
      RestoreAndReset("run became invalid");
      return true;
    }
    if (gameWorld.CurrentFrame <= session.FrozenFrame || player == null)
    {
      return false;
    }
    if (metronome.IsUiConsumingInput)
    {
      return false;
    }
    gameWorld.UnlockInputIfNeeded(player);
    if (!gameWorld.ValidInputWasTriggered(player))
    {
      return false;
    }

    metronome.Stop("first input accepted");
    clearTargetTick = true;
    session.PendingInputPlayer = player;
    session.UpdatingPlayer = player;
    runtimeRestorer.BeginPlaybackForInput(session, gameWorld.GetSecondsSinceInput(inputTick));
    return true;
  }

  internal void OnManualHitStarting(scrPlayer player, bool isAuto)
  {
    if (!IsFrozen || isAuto || player == null)
    {
      return;
    }
    visuals.RestorePlayer(player);
    if (session.PendingInputPlayer == player)
    {
      // Normally already started when the input was accepted; this covers the retried hit.
      runtimeRestorer.BeginPlaybackForInput(session, elapsedSinceInput: 0.0);
    }
  }

  internal void CompletePlayerUpdate(scrPlayer player)
  {
    if (!IsFrozen || player == null)
    {
      return;
    }
    if (session.UpdatingPlayer == player)
    {
      session.UpdatingPlayer = null;
    }
    if (session.PendingInputPlayer != player)
    {
      return;
    }
    if (gameWorld.CanRetryHit(player))
    {
      gameWorld.Hit(player);
    }
    if (!IsFrozen)
    {
      return;
    }

    // The input never moved the planet. Clear it so later frames do not retry without a key press,
    // and stop the song that was scheduled when the input was accepted.
    session.ClearPendingInput();
    if (session.PlaybackStartedForInput)
    {
      ResumeFrozenWait();
    }
  }

  internal void OnManualHitCompleted(scrPlayer player, bool isAuto, bool moved)
  {
    if (!IsFrozen || isAuto || player == null)
    {
      return;
    }
    if (!moved)
    {
      if (session.UpdatingPlayer == player)
      {
        // CompletePlayerUpdate retries this hit; keep playback scheduled so a successful retry does not
        // re-seek the song or lose the input-time anchor.
        return;
      }
      ResumeFrozenWait();
      return;
    }

    session.ClearPendingInput();
    ReleaseFrozenStart();
  }

  internal void PumpAsyncInput()
  {
    audioTimeline.PumpDiagnostics();
    if (!IsFrozen)
    {
      return;
    }
    if (!gameWorld.IsRuntimeValid(session.Controller))
    {
      RestoreAndReset("scene or editor state changed");
      metronome.ResetSessionSettings();
      return;
    }
    if (gameWorld.IsAsyncInputActive)
    {
      gameWorld.UpdateInput(session.Controller);
    }
  }

  internal void PumpFrozenVisuals()
  {
    if (IsWarming)
    {
      PumpWarmup();
      return;
    }
    if (IsFrozen)
    {
      metronome.UpdateDisplay();
      if (metronome.ConsumeDisableRequest())
      {
        RestartWithNativeCountdown();
        return;
      }
      visuals.UpdatePreLandingMotion();
    }
  }

  internal void OnPauseRequested(scrController controller)
  {
    if ((IsFrozen || IsWarming) && controller == session.Controller && gameWorld.IsPauseRequest(controller))
    {
      RestoreAndReset("pause requested");
    }
  }

  internal void Shutdown()
  {
    RestoreAndReset("mod shutdown");
    metronome.ResetSessionSettings();
  }

  internal void OnEditorPlayModeExited()
  {
    RestoreAndReset("editor play mode exited");
    if (restartingWithNativeCountdown)
    {
      restartingWithNativeCountdown = false;
      return;
    }
    metronome.ResetSessionSettings();
  }

  private void RestartWithNativeCountdown()
  {
    RestoreAndReset("metronome disabled");
    scnEditor editor = ADOBase.editor;
    if (editor == null)
    {
      return;
    }

    restartingWithNativeCountdown = true;
    try
    {
      editor.SwitchToEditMode();
      editor.Play();
    }
    catch (Exception exception)
    {
      restartingWithNativeCountdown = false;
      logger.LogError("Failed to restart the editor playtest with the native countdown", exception);
      metronome.ResetSessionSettings();
    }
  }

  private void ReleaseFrozenStart()
  {
    if (!IsFrozen)
    {
      return;
    }
    session.Phase = FrozenStartPhase.Releasing;
    runtimeRestorer.Restore(session, restartAudio: true);
    audioTimeline.RebaseAsyncInputClock();
    hitSounds.Reset(keepInstalledSchedule: true);
    ResetSession();
  }

  private void RestoreAndReset(string reason)
  {
    metronome.Stop(reason);
    if (
      session?.Phase
      is FrozenStartPhase.Warming
        or FrozenStartPhase.Frozen
        or FrozenStartPhase.Preparing
        or FrozenStartPhase.Releasing
    )
    {
      runtimeRestorer.Restore(
        session,
        restartAudio: session.Phase is FrozenStartPhase.Warming or FrozenStartPhase.Frozen or FrozenStartPhase.Preparing
      );
    }
    ResetSession();
  }

  private void PumpWarmup()
  {
    if (!gameWorld.IsRuntimeValid(session.Controller))
    {
      RestoreAndReset("run became invalid during visual warmup");
      return;
    }
    if (gameWorld.CurrentFrame <= session.WarmupStartedFrame)
    {
      return;
    }

    FrozenVisualWarmupSample sample = visuals.CaptureWarmupSample();
    bool tweenCountStable = session.WarmupTweenCount < 0 || sample.PausedTweenCount == session.WarmupTweenCount;
    bool frameDurationStable = DurationsAreStable(session.WarmupFrameDurationSeconds, sample.FrameDurationSeconds);
    session.WarmupStableFrames = tweenCountStable && frameDurationStable ? session.WarmupStableFrames + 1 : 1;
    session.WarmupTweenCount = sample.PausedTweenCount;
    session.WarmupFrameDurationSeconds = sample.FrameDurationSeconds;
    session.WarmupRenderedFrames++;
    session.WarmupStartedFrame = gameWorld.CurrentFrame;

    bool stable =
      session.WarmupRenderedFrames >= MinimumWarmupFrames && session.WarmupStableFrames >= MinimumWarmupFrames;
    if (!stable && session.WarmupRenderedFrames < MaximumWarmupFrames)
    {
      return;
    }

    session.FrozenFrame = gameWorld.CurrentFrame;
    session.Phase = FrozenStartPhase.Frozen;
    double metronomeStarted = session.StartupWatch.Elapsed.TotalMilliseconds;
    MetronomePlayback? playback = metronome.Start();
    visuals.StartPreLandingMotion(playback);
    LogStartupTimings(session, session.StartupWatch.Elapsed.TotalMilliseconds - metronomeStarted);
  }

  private void ResumeFrozenWait()
  {
    runtimeRestorer.RefreezeAfterRejectedInput(session);
    MetronomePlayback? playback = metronome.Start(continuePreviousGrid: true);
    visuals.StartPreLandingMotion(playback);
  }

  private void LogStartupTimings(FrozenStartSession startedSession, double metronomeMilliseconds)
  {
    FrozenStartTimings timings = startedSession.Timings;
    startedSession.StartupWatch.Stop();
    logger.Log(
      string.Format(
        CultureInfo.InvariantCulture,
        "Frozen start ready in {0:0.0} ms (game until music scheduled {1:0.0} ms; prepare {2:0.0} ms: "
          + "{3} automatic tiles {4:0.0} ms, visuals {5:0.0} ms, song prime {6:0.0} ms, hit sounds {7:0.0} ms; "
          + "warmup {8} frames; metronome {9:0.0} ms).",
        startedSession.StartupWatch.Elapsed.TotalMilliseconds,
        timings.MusicScheduledMilliseconds,
        timings.PrepareMilliseconds,
        timings.AutomaticTiles,
        timings.AutomaticTilesMilliseconds,
        timings.VisualScrubMilliseconds,
        timings.AudioPrimeMilliseconds,
        timings.HitSoundPrepareMilliseconds,
        startedSession.WarmupRenderedFrames,
        metronomeMilliseconds
      )
    );
  }

  private static bool DurationsAreStable(double previous, double current)
  {
    if (previous <= 0.0 || current <= 0.0)
    {
      return previous <= 0.0 && current <= 0.0;
    }
    double larger = Math.Max(previous, current);
    double smaller = Math.Min(previous, current);
    return (larger - smaller) / smaller <= WarmupFrameDurationTolerance;
  }

  private void ResetSession()
  {
    metronome.Stop();
    session = null;
  }
}
