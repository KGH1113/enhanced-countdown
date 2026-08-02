using EnhancedCountdown.Bootstrap;
using UnityEngine;

namespace EnhancedCountdown.Presentation;

[DefaultExecutionOrder(10000)]
internal sealed class RuntimeHost : MonoBehaviour
{
  private void Update()
  {
    ModCompositionRoot.Coordinator?.PumpAsyncInput();
  }

  private void LateUpdate()
  {
    ModCompositionRoot.Coordinator?.PumpFrozenVisuals();
  }

  private void OnDestroy()
  {
    ModCompositionRoot.Shutdown();
  }
}
