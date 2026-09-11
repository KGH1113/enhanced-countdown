# xPerfect 호환성 조사 노트

조사일: 2026-09-11

## 결론

현재 `EnhancedCountdown` 0.2.2는 xPerfect 브랜치에서 **로드는 되지만 기능 실행 중 깨진다**. 직접 원인은 xPerfect/ADOFAI 3.3.0에서 비동기 입력 틱 타입과 `scrPlayer.Hit` 시그니처가 변경된 것이다.

확인된 수정 범위는 작다. 핵심 코드는 4개 파일이며, 현재 설치본을 참조한 컴파일 오류는 5개다. 다만 단순 컴파일 성공 뒤에도 동기/비동기 입력과 자동 타일을 실제 게임에서 각각 검증해야 한다.

## 조사 기준 환경

- Steam 브랜치: `xperfect`
- Steam build ID: `25202159`
- 게임 버전: `3.3.0`
- 게임 Unity 버전: `6000.3.21f1`
- Unity Mod Manager: `0.32.5.0`
- 설치된 Enhanced Countdown: `0.2.2`
- 조사한 `Assembly-CSharp.dll` SHA-256: `984c648956d02ffadc6e2041082be7f475c313ff04ffa24457f36a391ea309f5`

Steam manifest의 브랜치 설명은 `XPerfect judgment update testing`이다. 커뮤니티 호환성 허브도 ADOFAI 3.3.0용 임시 패치들을 별도로 안내하고 있어, 이번 변경이 여러 UMM 모드에 영향을 주는 게임 API 변경임을 뒷받침한다: [ADOFAI Mod Compatibility Hub](https://github.com/Building114/ADOFAI-Mod-Compatibility-Hub).

## 확정된 API 변경

### 1. 비동기 입력 틱이 `ulong`에서 `long`으로 변경됨

xPerfect의 실제 어셈블리:

```csharp
public static long AsyncInputManager.currFrameTick;
public static long AsyncInputManager.prevFrameTick;
public static long AsyncInputManager.offsetTick;
public static long AsyncInputManager.targetSongTick;

public void scrPlayer.Simulated_PlayerControl_Update(long? targetTick = null)
```

현재 모드:

- `PlayerInputPatches.cs`: Harmony prefix의 `targetTick`가 `ref ulong?`
- `MidRunCoordinator.cs`: `PreparePlayerUpdate`가 `ref ulong?`
- `AdofaiAudioTimeline.cs`: 재기준화 계산 및 필드 할당이 모두 `ulong`

설치된 0.2.2 바이너리 역시 `AsyncInputManager`의 세 필드를 `unsigned int64`로 참조한다.

실제 플레이 로그에서 첫 입력 뒤 다음 예외가 재현됐다.

```text
MissingFieldException: Field not found: ulong .AsyncInputManager.prevFrameTick
```

스택은 `ReleaseFrozenStart -> RebaseAsyncInputClock` 경로를 가리킨다. 따라서 UMM에 `Active`로 표시되고 Harmony 패치가 설치되더라도 기능 호환성은 확보되지 않는다.

### 2. `scrPlayer.Hit`에 `hitTick` 인자가 추가됨

이전 모드가 기대하는 시그니처:

```csharp
bool scrPlayer.Hit(bool isAuto)
```

xPerfect의 실제 시그니처:

```csharp
public bool Hit(long? hitTick, bool isAuto = false)
```

현재 직접 호출 두 곳은 새 어셈블리를 참조해 컴파일되지 않는다.

- `AdofaiGameWorld.AdvanceAutomaticTiles`: `player.Hit(isAuto: true)`
- `AdofaiGameWorld.Hit`: `player.Hit(isAuto: false)`

새 `hitTick`는 값이 있으면 `AsyncInputManager.offsetTick`와 함께 초 단위 판정을 계산하고, `null`이면 기존 각도 기반 판정을 사용한다. Enhanced Countdown은 첫 입력 시 타임라인과 행성 각도를 의도적으로 정확한 시점으로 재기준화하고 기존에도 입력 틱을 `null`로 지웠으므로, 이 두 직접 호출에는 `Hit(null, isAuto: ...)`로 기존 의미를 보존하는 것이 가장 자연스럽다.

## 빌드 재현 결과

xPerfect 설치본의 `Managed` 디렉터리를 참조해 코어 프로젝트를 빌드하면 오류 5개가 난다.

| 개수 | 위치 | 원인 |
|---:|---|---|
| 3 | `AdofaiAudioTimeline.cs` | `ulong` 값을 새 `long` 필드에 암시적으로 할당할 수 없음 |
| 2 | `AdofaiGameWorld.cs` | `scrPlayer.Hit(long? hitTick, bool isAuto)`의 필수 첫 인자 누락 |

그 외 공개 API 참조는 컴파일 단계에서 유지되는 것으로 확인됐다.

## Harmony 및 리플렉션 대상 점검

다음 패치 대상은 xPerfect 어셈블리에도 존재하며, 이름과 주요 인자도 유지된다.

- `scrController.Start_Rewind(int)`
- `scrController.Scrub(int, bool)`
- `scrController.OnMusicScheduled()`
- `scrController.TogglePauseGame()`
- `scnEditor.SwitchToEditMode(bool)`
- `scrConductor.DesyncFix()`
- `scrPlayer.Simulated_PlayerControl_Update(long?)`
- `scrPlayer.Hit(long?, bool)`

히트 사운드 스케줄러가 리플렉션으로 읽고 쓰는 다음 필드도 모두 남아 있다.

- `scrConductor.hitSoundsData`
- `scrConductor.holdSoundsData`
- `scrConductor.extraTicksCountdown`
- `scrConductor.countdownTimes`
- `scrConductor.playEndingCymbal`

`FirstManualHitPatch`는 원본의 `isAuto`와 `__result`만 받으므로 새 `hitTick` 인자를 반드시 받을 필요는 없다. 구버전과 xPerfect를 한 바이너리로 지원하려면 `FrozenPlayerUpdatePatch`가 구체적인 `ulong?`/`long?` 타입에 결합되지 않도록 Harmony의 `object[] __args`로 첫 인자를 지워야 한다.

## Unity/AssetBundle 영향

게임은 Unity `6000.3.21f1`, UI 프로젝트는 `6000.3.10f1`이다. 현재 macOS 번들은 xPerfect 플레이어에서 실제로 로드되고 프리팹 인스턴스화까지 진행되므로 이번 장애의 직접 원인은 아니다.

Unity 문서상 구버전 Editor에서 만든 번들을 신버전 Player가 읽는 방향은 TypeTree를 통한 하위 호환이 일반적으로 지원되지만, 큰 직렬화 변경이 있으면 재빌드가 필요할 수 있다. 반대 방향인 신버전 번들 → 구버전 Player는 지원되지 않는다: [Unity AssetBundle backward compatibility](https://docs.unity3d.com/2023.2/Documentation/Manual/AssetBundlesIntro.html).

로그에는 `MetronomePanelPreviewHarness`가 누락된 스크립트라는 경고가 별도로 있다. UI는 계속 생성되며 xPerfect API 변경과 직접 관련은 없지만, 프리팹/번들 정리 항목으로 따로 추적할 가치가 있다.

## 적용한 호환 패치

버전 문자열은 검사하지 않는다. `AdofaiRuntimeApi`가 런타임 멤버 자체를 reflection으로 탐색한다.

1. `scrPlayer.Simulated_PlayerControl_Update(long?)`를 먼저 찾고, 없으면 구형 `ulong?` 시그니처를 선택한다.
2. `scrPlayer.Hit(long?, bool)`, `Hit(ulong?, bool)`, `Hit(bool)` 순서로 실제 존재하는 메서드를 선택한다.
3. `prevFrameTick`, `currFrameTick`, `offsetTick`의 실제 필드 타입이 `long`인지 `ulong`인지 확인하고 그 타입으로 값을 기록한다.
4. Harmony prefix는 `object[] __args`를 사용해 어느 nullable 정수 타입에도 고정 결합하지 않고, 필요한 경우 첫 입력 틱을 `null`로 지운다.
5. 생성된 모드 DLL에 변경된 메서드/필드의 고정 MemberRef가 남지 않았는지 검사한다.

실제 게임에서는 아래 매트릭스를 확인해야 한다.

| 경로 | 확인 사항 |
|---|---|
| 동기 입력 | 중간 시작, 첫 입력, 거절된 입력 재시도, 일시정지 |
| 비동기 입력 | 첫 입력 후 예외 없음, 다음 타일 입력 정상, 판정/오디오 동기화 |
| 자동 타일 포함 구간 | 준비 중 자동 타일 통과 및 다음 수동 타일 정지 |
| 메트로놈 비활성화 | 네이티브 카운트다운으로 정상 재시작 |
| 에디터 종료/재시작 | 오디오·TimeScale·시각 효과 복구 |

## 현재 판단

- 필수 코드 수정: 작고 명확함
- 회귀 위험: 중간. 컴파일 수정 자체는 단순하지만 입력 틱은 판정 정확도와 직접 연결됨
- AssetBundle 재빌드: 당장 필수라는 증거 없음
- 릴리스 전 실게임 검증: 필수
- 기존 3.2 계열까지 한 바이너리로 지원: reflection 호환 계층을 적용했으며, 생성 DLL에서 새/구형 시그니처에 대한 고정 MemberRef가 제거됨
