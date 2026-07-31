using RemoveCountdown.Application.MidRun;
using RemoveCountdown.Infrastructure.Adofai;
using RemoveCountdown.Infrastructure.Unity;
using UnityModManagerNet;

namespace RemoveCountdown.Bootstrap;

internal static class ModCompositionRoot
{
  internal static MidRunCoordinator Coordinator { get; private set; }

  internal static void Initialize(UnityModManager.ModEntry entry)
  {
    Shutdown();
    var logger = new ModLogger(entry);
    var gameWorld = new AdofaiGameWorld(logger);
    var audioTimeline = new AdofaiAudioTimeline(logger);
    var hitSounds = new AdofaiHitSoundScheduler(new ConductorHitSoundAccessor(), logger);
    var metronome = new UnityFrozenMetronome(logger);
    var visuals = new UnityFrozenVisuals();
    var startPreparer = new FrozenStartPreparer(gameWorld, audioTimeline, metronome, visuals, logger);
    var runtimeRestorer = new FrozenRuntimeRestorer(audioTimeline, hitSounds, visuals, logger);
    Coordinator = new MidRunCoordinator(
      gameWorld,
      audioTimeline,
      metronome,
      visuals,
      logger,
      startPreparer,
      runtimeRestorer
    );
  }

  internal static void Shutdown()
  {
    Coordinator?.Shutdown();
    Coordinator = null;
  }
}
