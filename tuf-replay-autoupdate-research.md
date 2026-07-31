# TUFReplay 자동 업데이트 조사

조사 대상: `/Users/kgh/dev/src/tuf-replay` (`dev`, HEAD `8b3e3443`)

조사 방식: 소스, 패키징 스크립트, 테스트, Git 이력을 정적 분석했다. 대상 저장소는 수정하지 않았고 빌드/테스트는 실행하지 않았다.

## 결론

TUFReplay의 자동 업데이트는 **The Update Framework(TUF)를 구현한 것이 아니다.** GitHub Releases와 HTTPS를 신뢰 루트로 삼아 자체 JSON 매니페스트와 SHA-256으로 전체 런타임 ZIP을 검증하는 구조다.

아키텍처의 핵심은 루트에 고정된 `TUFReplay.Bootstrap.dll`과 `Runtime/versions/<version>` 아래의 버전별 `TUFReplay.UpdateEngine.dll`/payload 분리다. 현재 버전의 엔진이 다음 버전을 받고, 새 payload의 초기화가 성공하면 `state.json` 포인터를 승격한다. 현재/이전 두 런타임을 보존하고 손상된 현재 디렉터리는 이전 버전으로 복구한다.

전체-runtime 단위 설치, 크기/해시 검증, ZIP 경로 탈출 방지, 후보 경로 재검증, 원자적 상태 파일 교체 등 기본기는 탄탄하다. 가장 큰 운영상 문제는 **실패한 릴리스 격리/보류가 없다는 점**이다. 새 payload 초기화가 실패하면 현재 런타임을 같은 실행에서 대신 로드하지 않고 `false`를 반환하며, 후보를 삭제한 뒤 다음 실행에 같은 최신 릴리스를 다시 다운로드한다. 불량 릴리스가 내려가 있으면 온라인 상태에서 매 실행마다 업데이트 실패가 반복될 수 있다.

## 도입 이력

- `4be9305b` — 2026-07-15: 시작 시 자동 업데이트 도입
- `cc226414` — 2026-07-17: 빌드/패키징 스크립트를 계층형 workflow로 분리
- `91510a5d` — 2026-07-25: 업데이트 엔진을 고정 부트스트랩에서 버전별 런타임으로 분리

현재 README는 beta.3을 전체-runtime 업데이터의 수동 설치 baseline으로 설명한다. 이후 버전은 제자리 업데이트 가능하다.

## 구성 요소

| 구성 요소 | 역할 | 위치 |
|---|---|---|
| 고정 launcher | 상태 복구, 업데이트 엔진 호출, 후보 payload 시험 로드, 승격 | `TUFReplay.Bootstrap/Bootstrap.cs` |
| 런타임 저장소 | `state.json`, current/previous/trial, 버전 보존/정리 | `TUFReplay.Bootstrap/RuntimeStore.cs` |
| 업데이트 엔진 loader | 현재 런타임의 updater DLL을 reflection으로 실행 | `TUFReplay.Bootstrap/UpdateEngineLoader.cs` |
| 버전별 updater | 릴리스 선택, 다운로드, 검증, 압축 해제 및 후보 설치 | `TUFReplay.UpdateEngine/UpdateManager.cs` |
| 매니페스트 parser | schema, 버전, 크기, SHA-256, runtimePath 검증 | `TUFReplay.UpdateEngine/ReleaseManifest.cs` |
| 패키징 | 루트 launcher + 버전별 runtime ZIP과 매니페스트 생성 | `scripts/tasks/package/stage.sh`, `write-release-assets.sh` |

배포 ZIP에는 루트 launcher도 포함되지만 자동 업데이트 시 updater는 매니페스트의 `runtimePath`만 `Runtime/versions/<version>`으로 이동한다. 따라서 `TUFReplay.UpdateEngine.dll`은 업데이트되지만 루트의 `TUFReplay.Bootstrap.dll`은 자동으로 교체되지 않는다.

## 실행 흐름

1. Unity Mod Manager가 루트의 고정 Bootstrap을 로드한다.
2. `RuntimeStore.LoadAndRepair()`가 `state.json`을 읽는다.
   - 이전 실행의 `Trial`이 남아 있으면 후보를 삭제하고 trial을 해제한다.
   - `Current` 런타임의 필수 파일이 없으면 `Previous`로 포인터를 되돌린다.
   - current/previous/trial 이외 버전 디렉터리를 제거한다.
3. Bootstrap이 현재 런타임의 `TUFReplay.UpdateEngine.dll`을 reflection으로 로드한다.
4. 업데이트 엔진은 20초 제한으로 릴리스를 찾는다.
5. stable 채널은 GitHub의 `releases/latest/download/`에서 고정 이름의 두 asset을 요청한다.
6. beta 채널은 Releases API의 최근 20개를 보고 draft를 제외한 가장 높은 SemVer tag 중 두 asset이 모두 있는 릴리스를 고른다.
7. `TUFReplay.update.json`을 최대 16 KiB로 받고 schema/version/asset name/size/hash/runtime path를 검증한다.
8. 현재 버전보다 높지 않으면 기존 런타임을 로드한다. 이미 설치된 동일 후보가 구조 검사를 통과하면 다운로드를 생략한다.
9. `TUFReplay.zip`을 최대 128 MiB로 다운로드하고 매니페스트의 byte 수와 SHA-256을 확인한다.
10. 임시 디렉터리에 압축을 풀면서 전체 extracted size를 256 MiB로 제한하고 ZIP traversal을 차단한다.
11. 후보 안의 `TUFReplay.dll`, `TUFReplay.UpdateEngine.dll`, `Info.json` 존재 및 버전을 확인한 뒤 버전 디렉터리로 이동한다.
12. Bootstrap이 경로를 다시 검증하고 `Trial`을 저장한 뒤 새 payload의 `TUFReplay.Main.Load`를 실행한다.
13. 성공하면 기존 current를 previous로 옮기고 후보를 current로 승격한다. 실패하면 trial과 후보를 제거하고 그 실행의 모드 로드를 실패시킨다.

## 채널과 버전 정책

- 기본값은 stable이며 `UpdateSettings.json` 파싱 실패도 stable로 fail-closed 된다.
- `ReceiveBetaUpdates=true`이면 stable과 prerelease tag를 모두 후보로 보고 SemVer상 가장 높은 버전을 선택한다.
- beta를 끄더라도 설치된 beta보다 낮은 stable로 자동 downgrade하지 않는다.
- tag의 앞 `v`/`V`는 제거하고 build metadata는 비교에서 무시한다.
- 구현은 엄격한 SemVer보다 관대하다. core 1~4개와 선행 0을 허용한다.
- beta 채널은 GitHub API 응답의 최근 20개만 살피므로 게시 시간과 버전 순서가 크게 어긋나면 전역 최고 버전을 놓칠 수 있다.

## 무결성과 신뢰 경계

### 잘 된 부분

- 매니페스트와 ZIP에 각각 크기 상한이 있다.
- HTTP `Content-Length`, 실제 수신 byte 수, 매니페스트의 package size를 대조한다.
- 전체 ZIP의 SHA-256을 검증한 뒤에만 설치한다.
- 압축 해제 총량 제한과 canonical path prefix 검사를 통해 zip bomb/traversal 위험을 줄인다.
- 매니페스트의 runtimePath도 추출 root 밖으로 나갈 수 없다.
- beta API에서 받은 asset URL은 production에서 HTTPS `github.com`만 허용한다.
- Bootstrap이 updater가 반환한 경로를 예상 버전 디렉터리와 다시 비교한다.
- 설정과 `Data/`는 버전 디렉터리 밖에 있어 runtime 교체/정리에서 보존된다.

### 실제 신뢰 모델

매니페스트 자체의 서명은 없다. ZIP 해시는 전송 손상과 서로 다른 asset 조합을 탐지하지만, 그 해시를 제공하는 매니페스트도 같은 GitHub Release에서 온다. 따라서 GitHub 저장소/release 권한이나 게시 파이프라인이 탈취되면 공격자는 새 매니페스트와 ZIP을 함께 올려 임의 코드를 실행할 수 있다. 별도 signing key, threshold signature, metadata expiry, key rotation, rollback/freeze 방어는 없다.

즉 현재 보안은 “GitHub 계정/조직 보안 + GitHub HTTPS + release asset 무결성”에 의존한다. 프로젝트명 때문에 TUF 수준의 공급망 보장을 기대하면 안 된다.

## 실패와 복구 동작

| 실패 지점 | 현재 동작 |
|---|---|
| 네트워크/HTTP/매니페스트/ZIP 오류 | 경고 후 현재 runtime 로드 |
| 20초 timeout | 취소 요청 후 현재 runtime 로드 |
| 후보 payload 초기화 실패 | 후보 삭제, current 포인터 유지, 그러나 같은 실행에서는 current를 로드하지 않고 모드 로드 실패 |
| 후보 로드 성공 후 state 승격 저장 실패 | 그 실행은 새 payload로 계속 실행; 다음 실행에서 남은 trial을 폐기하고 이전 current 사용 가능 |
| 다음 실행에서 current 필수 파일 누락 | previous가 유효하면 previous로 rollback |
| current DLL 내용 손상(파일은 존재) | 구조 검사를 통과할 수 있고 payload 로드 실패 시 자동 previous fallback 없음 |
| `state.json` 없음 | `.bak`이 있으면 복구; 둘 다 없으면 beta.3 수동 설치 요구 |

`Trial`은 장시간 health check가 아니라 `TUFReplay.Main.Load` 호출 한 번의 성공 여부만 의미한다. Load 이후 첫 프레임, DB migration 후속 작업, helper 기동 등에서 문제가 나도 이미 current로 승격된 상태다.

## 주요 위험과 개선 우선순위

### P1 — 실패한 최신 릴리스가 반복적으로 모드 로드를 막음

후보 초기화 실패 시 current 포인터는 보존되지만 같은 실행에서 current payload를 로드하지 않는다. 후보는 삭제되므로 다음 실행에 다시 다운로드하고 동일 실패를 반복한다. README의 “다음 실행에 최신 릴리스를 재시도”가 그대로 무한 retry 정책이다.

권장:

1. `state.json`에 `RejectedVersion`, `RejectedAt`, `FailureCount`를 기록한다.
2. 같은 버전은 일정 횟수/기간 동안 건너뛰고 current를 정상 로드한다.
3. 가능하면 실패 직후 current를 fallback 로드한다. 현재 assembly resolver가 trial 디렉터리로 전환된 뒤라 안전한 same-process fallback이 어려우므로 loader isolation 또는 “다음 실행에서 current 강제, candidate 보류” 같은 설계가 필요하다.

### P1 — 독립적인 release 서명 없음

GitHub release 권한 탈취가 곧 클라이언트 코드 실행으로 이어진다.

권장:

- 최소 단계: 오프라인 보관 Ed25519 release key로 canonical manifest에 서명하고 공개키를 고정 Bootstrap에 내장한다.
- 강한 단계: 실제 TUF를 도입해 root/targets/snapshot/timestamp 역할, expiry, key rotation과 threshold를 사용한다.
- GitHub Actions에서 package 생성과 release 게시를 자동화하고 OIDC/환경 보호, immutable release 절차를 둔다.

### P1 — updater 테스트가 GitHub CI에서 실행되지 않음

`TUFReplay.UpdateTests/Program.cs`에는 매니페스트, retention/trial recovery, 전체 package 설치, checksum/size mismatch, unsafe ZIP, beta 선택 테스트가 있다. 하지만 현재 `.github/workflows/ci.yml`은 Bun web check만 실행한다. C# 테스트는 로컬 macOS build workflow에 묶여 있다.

권장:

- updater/bootstrap의 순수 테스트를 게임/SQLite/macOS helper 의존성에서 분리해 Linux GitHub CI에서 항상 실행한다.
- release asset 생성 후 동일 ZIP/manifest로 end-to-end updater test를 실행한다.

### P2 — 20초 deadline 이후 설치 작업이 백그라운드에서 계속될 수 있음

timeout 시 cancellation을 요청하지만 ZIP 해제, checksum, directory move는 동기 작업이고 cancellation token을 확인하지 않는다. deadline 직전에 다운로드가 끝나 추출에 들어가면 caller는 current를 로드하면서 background task가 새 runtime 디렉터리를 계속 변경할 수 있다.

권장:

- 다운로드 timeout과 전체 설치 deadline을 분리한다.
- hash/extract loop에도 cancellation check를 넣는다.
- resolve task를 버리지 말고 명확히 종료/정리한 뒤 current를 로드한다.

### P2 — 설치 후/부팅 시 검증이 구조 수준에 그침

런타임 검증은 필수 파일 존재와 `Info.json` 버전만 확인한다. 설치 당시 ZIP 전체 해시는 안전하지만 이후 디스크 손상/로컬 변조를 탐지하지 못한다. current DLL이 존재하지만 깨졌으면 previous 자동 복구 경로도 작동하지 않는다.

권장:

- runtime 내부 파일 목록과 hash manifest를 함께 저장하고 부팅 시 핵심 파일을 검증한다.
- current payload load 실패 시 next-boot rollback marker를 남겨 previous를 선택한다.

### P2 — 고정 Bootstrap은 자동으로 고칠 수 없음

업데이트 엔진을 버전별로 분리한 덕분에 대부분의 updater 로직은 자체 업데이트 가능하다. 반대로 `RuntimeStore`, trial/promotion, payload loader의 버그는 자동 업데이트 ZIP에 새 Bootstrap이 들어 있어도 적용되지 않는다. 수동 baseline 재설치가 필요하다.

권장:

- Bootstrap API와 책임을 가능한 작게 유지하고 집중 테스트한다.
- Bootstrap 최소 지원 버전을 manifest에 명시해 호환되지 않는 release는 설치 전에 거부하고 수동 업데이트 안내를 표시한다.
- 향후 launcher 교체가 필요하다면 게임 종료 후 별도 helper가 원자적으로 교체하는 명시적 migration 경로를 설계한다.

### P2 — stable 채널은 release tag와 manifest version을 대조하지 않음

beta API 경로는 tag와 manifest version을 비교하지만 stable의 `latest/download` 경로는 예상 tag가 `null`이라 비교가 없다. 잘못 게시된 latest asset의 높은 manifest version을 그대로 신뢰한다.

권장:

- stable도 Releases API에서 latest non-prerelease release와 asset을 선택해 tag/version을 대조한다.

### P3 — release 게시 자동화/검증 연결이 보이지 않음

패키징 스크립트는 `TUFReplay.zip`과 `TUFReplay.update.json`을 생성하지만 저장소의 GitHub workflow에는 release 생성/asset 업로드 단계가 없다. README도 두 파일을 release에 첨부해야 한다고만 설명한다. 수동 게시라면 두 asset 불일치나 누락 위험이 높다.

권장:

- tag, `Info.json`, manifest version의 일치를 검증한 뒤 두 asset을 한 workflow에서 게시한다.
- 게시 직후 stable/beta discovery endpoint를 통해 실제 클라이언트와 같은 검증을 수행한다.

## 테스트 보강 목록

현재 7개 updater 테스트에 다음을 추가하는 것이 좋다.

- Bootstrap candidate load 실패 후 current 가용성 및 rejected-version 정책
- process crash로 `Trial`이 남은 경우와 promotion 저장 실패
- current 내용 손상 시 previous rollback
- stable release tag/manifest mismatch
- malicious/invalid runtimePath, absolute path, mixed separators, duplicate ZIP entry
- 압축 해제 256 MiB 초과와 package 128 MiB 초과
- redirect host/scheme 정책
- 20초 경계에서 취소 중 extraction/move가 겹치는 race
- prerelease/stable 경계, build metadata, API 20개 pagination
- 기존 버전 디렉터리가 손상/잠김/읽기 전용일 때 설치 원자성
- release package로부터 실제 Bootstrap → UpdateEngine → Payload까지 이어지는 end-to-end test

## 권장 실행 순서

1. 불량 버전 quarantine과 next-boot rollback을 먼저 넣어 운영상 boot loop를 막는다.
2. updater 테스트를 독립 CI job으로 올리고 failure/cancellation/race 테스트를 추가한다.
3. stable 선택도 API 기반으로 바꾸고 tag-manifest 일치를 강제한다.
4. release asset 생성·게시·사후 검증을 하나의 보호된 workflow로 묶는다.
5. threat model에 맞춰 manifest 서명 또는 실제 TUF 도입을 결정한다.
6. Bootstrap 호환성/version contract와 수동 migration 경로를 문서화한다.

## 근거 파일

- `/Users/kgh/dev/src/tuf-replay/TUFReplay.Bootstrap/Bootstrap.cs`
- `/Users/kgh/dev/src/tuf-replay/TUFReplay.Bootstrap/RuntimeStore.cs`
- `/Users/kgh/dev/src/tuf-replay/TUFReplay.Bootstrap/UpdateEngineLoader.cs`
- `/Users/kgh/dev/src/tuf-replay/TUFReplay.Bootstrap/PayloadLoader.cs`
- `/Users/kgh/dev/src/tuf-replay/TUFReplay.UpdateEngine/UpdateManager.cs`
- `/Users/kgh/dev/src/tuf-replay/TUFReplay.UpdateEngine/ReleaseManifest.cs`
- `/Users/kgh/dev/src/tuf-replay/TUFReplay.UpdateEngine/SemanticVersion.cs`
- `/Users/kgh/dev/src/tuf-replay/TUFReplay.UpdateTests/Program.cs`
- `/Users/kgh/dev/src/tuf-replay/scripts/tasks/package/stage.sh`
- `/Users/kgh/dev/src/tuf-replay/scripts/tasks/package/write-release-assets.sh`
- `/Users/kgh/dev/src/tuf-replay/.github/workflows/ci.yml`
- `/Users/kgh/dev/src/tuf-replay/README.md`
