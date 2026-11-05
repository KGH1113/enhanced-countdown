namespace EnhancedCountdown.Infrastructure.Compatibility;

// While OrbitRender renders, it drives the conductor with its own render clock.
// A frozen start reschedules the song on Unity's DSP clock instead, which moves
// the rendered song away from the picture and from replay media.
internal static class OrbitRenderActivityDetector
{
  private const string AssemblyName = "OrbitRender";

  // Public API for other mods, available since OrbitRender 2.6.1.
  private static readonly StaticBoolProperty RenderApiBusy = new(AssemblyName, "OrbitRender.RenderApi", "IsBusy");

  // Older releases only expose whether the renderer owns the game clock.
  private static readonly StaticBoolProperty RendererControlsTime = new(
    AssemblyName,
    "OrbitRender.Renderer.RendererController",
    "ControlsTime"
  );

  internal static bool IsRendering() => (RenderApiBusy.Read() ?? RendererControlsTime.Read()) == true;
}
