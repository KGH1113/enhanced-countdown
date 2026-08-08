using EnhancedCountdown.Domain.MidRun;

namespace EnhancedCountdown.Application.Ports;

internal interface IGameWorld
{
  int ResolveStartFloor(int requestedFloor);
  string GetNativeCountdownFallbackReason();
  bool CanArm(scrController controller, int startFloor);
  bool CanPrepareInitialScrub(scrController controller, int floorNumber);
  bool CanHandleMusicScheduled(scrController controller);
  bool IsRuntimeValid(scrController controller);
  bool IsPauseRequest(scrController controller);
  int CurrentFrame { get; }
  int AutomaticTileSafetyLimit { get; }
  bool IsAsyncInputActive { get; }

  void EnterPlayerControl(scrController controller);
  scrPlayer GetPrimaryPlayer(scrController controller);
  bool HasChosenPlanet(scrPlayer player);
  bool IsNextTileAutomatic(scrPlayer player);
  void AdvanceAutomaticTiles();
  bool HasFollowingTile(scrPlayer player);
  int GetCurrentFloorId(scrPlayer player);
  PerfectTimingInput GetPerfectTimingInput(scrPlayer player, double crotchet);
  void SeekLoadedWorld(double logicalSongPosition, double audioSongPosition);

  bool UnlockInputIfNeeded(scrPlayer player);
  bool ValidInputWasTriggered(scrPlayer player);
  string DescribeInput(scrPlayer player);
  bool CanRetryHit(scrPlayer player);
  bool Hit(scrPlayer player);
  void UpdateInput(scrController controller);
}
