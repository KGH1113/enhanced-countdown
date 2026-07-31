using RemoveCountdown.Bootstrap;
using UnityEngine;

namespace RemoveCountdown.Presentation;

internal sealed class RuntimeHost : MonoBehaviour
{
  private void Update()
  {
    ModCompositionRoot.Coordinator?.PumpAsyncInput();
  }

  private void OnDestroy()
  {
    ModCompositionRoot.Shutdown();
  }
}
