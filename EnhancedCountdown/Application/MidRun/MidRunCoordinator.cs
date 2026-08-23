using System;
using System.Diagnostics;
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
      logger.Log($"Using the game's native countdown because {fallbackReason}.");
      return;
    }
    fallbackReason = hitSounds.GetCompatibilityFailureReason();
    if (fallbackReason != null)
    {
      logger.Log($"Using the game's native countdown because {fallbackReason}.");
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

  internal bool PreparePlayerUpdate(scrPlayer player, ref ulong? targetTick)
  {
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
    if (gameWorld.UnlockInputIfNeeded(player))
    {
      logger.Log("Released the inherited player input lock after the launch frame.");
    }
    if (!gameWorld.ValidInputWasTriggered(player))
    {
      return false;
    }

    metronome.Stop("first input accepted");
    targetTick = null;
    session.PendingInputPlayer = player;
    session.InputResumeStartedTimestamp = Stopwatch.GetTimestamp();
    runtimeRestorer.ReleaseAudioForInput(session);
    logger.Log(gameWorld.DescribeInput(player));
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
      runtimeRestorer.RebaseTimelineForInput(session);
    }
  }

  internal void CompletePlayerUpdate(scrPlayer player)
  {
    if (!IsFrozen || player == null || session.PendingInputPlayer != player)
    {
      return;
    }
    if (!gameWorld.CanRetryHit(player))
    {
      session.ClearPendingInput();
      return;
    }

    logger.Log("The original input update did not land; retrying the same input through Hit(false).");
    if (!gameWorld.Hit(player))
    {
      logger.Log("The fallback Hit(false) was rejected; keeping the frozen start active.");
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
      runtimeRestorer.RefreezeAfterRejectedInput(session);
      MetronomePlayback? playback = metronome.Start();
      visuals.StartPreLandingMotion(playback);
      return;
    }

    logger.Log("The first input landed naturally at the frozen Pure Perfect angle.");
    long inputResumeStartedTimestamp = session.InputResumeStartedTimestamp;
    int warmupRenderedFrames = session.WarmupRenderedFrames;
    int warmupTweenCount = session.WarmupTweenCount;
    session.ClearPendingInput();
    ReleaseFrozenStart();
    double resumeMilliseconds = (Stopwatch.GetTimestamp() - inputResumeStartedTimestamp) * 1000.0 / Stopwatch.Frequency;
    logger.Log(
      $"Accepted frozen input after prepared resume work in {resumeMilliseconds:F3} ms; "
        + $"warmupFrames={warmupRenderedFrames}, tweens={warmupTweenCount}."
    );
  }

  internal void PumpAsyncInput()
  {
    hitSounds.Pump();
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
    logger.Log("Metronome disabled for this editor playtest; restarting with the game's native countdown.");
    RestoreAndReset("metronome disabled");
    scnEditor editor = ADOBase.editor;
    if (editor == null)
    {
      logger.Log("Could not restart with the native countdown because the level editor is unavailable.");
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
    logger.Log("Resumed the loaded run from the frozen Pure Perfect timestamp.");
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
      logger.Log($"Cleared frozen start state: {reason}.");
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
    MetronomePlayback? playback = metronome.Start();
    visuals.StartPreLandingMotion(playback);
    double warmupMilliseconds =
      (DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond - session.WarmupStartedRealtime) * 1000.0;
    logger.Log(
      $"Completed frozen visual warmup: frames={session.WarmupRenderedFrames}, "
        + $"stableFrames={session.WarmupStableFrames}, tweens={session.WarmupTweenCount}, "
        + $"elapsedMs={warmupMilliseconds:F3}."
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
