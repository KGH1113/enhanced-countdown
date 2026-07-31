using UnityEngine;

namespace RemoveCountdown;

internal sealed class RuntimeHost : MonoBehaviour
{
  private void Update()
  {
    MidRunState.PumpAsyncInput();
  }

  private void OnDestroy()
  {
    MidRunState.Shutdown();
  }
}
