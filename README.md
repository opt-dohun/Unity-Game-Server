# 붉은 파도 — 서버 판정 턴제 보스전

## 구조

| 디렉터리 | 내용 |
| --- | --- |
| `Server` | ASP.NET Core HTTP API, 전투 규칙, API 통합 검사 |
| `Client/UnityGame` | Unity 6000.6.3f1 게임과 macOS 빌드 |
| `Client/Browser` | 동일 API를 사용하는 브라우저 미리보기 |
| `Infra` | SQLite 테이블과 데이터 접근 코드 |
| `Server/LegacyC` | 이전 C 프로토타입과 PCG32 학습 예제. 현재 게임에서는 사용하지 않음 |

## 실행

.NET 10 SDK가 필요합니다. 프로젝트 루트에서 서버를 실행하세요.

```sh
dotnet run --project Server/CrimsonTide.Server.csproj
```

서버는 `127.0.0.1:8001`에서 듣고 `Server/data/crimson-tide.db`에 전투와 랭킹을 저장합니다. 저장 경로는 `CRIMSON_DB_PATH` 환경 변수로 변경할 수 있습니다. 이 서버를 켜야 전투를 시작할 수 있습니다.

Unity는 `Client/UnityGame/Assets/Scenes/BossBattle.unity`를 열어 Play를 누르거나 `Client/UnityGame/Builds/CrimsonTide-TurnBased.app`을 실행하세요. 브라우저 미리보기는 프로젝트 루트에서 다음 명령으로 열 수 있습니다.

```sh
python3 -m http.server 8000
```

주소는 `http://localhost:8000/`입니다. 조작은 공격 `1`/`J`, 방어 `2`/`K`입니다. 공격 피해는 1–20이고 방어는 보스 피해를 80% 줄입니다.

## 서버 판정과 중복 처리

1. `POST /api/battles` — 빈 JSON과 `Idempotency-Key` 헤더로 전투를 시작합니다. 같은 키로 재시도하면 같은 전투를 반환합니다.
2. `POST /api/battles/{battleId}/turns` — `{ "turn": 1, "action": "attack" }` 또는 `guard`를 전송합니다. 서버가 PCG32 난수로 피해량을 계산하고 양쪽 체력, 패턴, 종료 여부를 반환합니다.
3. 같은 `battleId + turn + action`을 다시 보내면 저장된 응답을 그대로 돌려줍니다. 이미 처리한 턴에 다른 행동을 보내거나 턴 순서를 건너뛰면 `409 Conflict`입니다.
4. 승리 후 `POST /api/battles/{battleId}/score`에 `{ "name": "기사" }`를 보냅니다. 턴 수와 체력은 서버의 전투 기록에서 가져옵니다. 한 전투의 기록은 한 번만 등록됩니다.
5. `GET /api/scores`는 적은 턴 수, 높은 남은 체력 순의 상위 20개를 반환합니다.

턴 판정·상태 변경·결과 저장은 SQLite 트랜잭션에서 함께 처리합니다. 서버를 재시작해도 전투 상태와 처리한 턴의 응답이 남습니다. 현재는 단일 서버 프로세스용 로컬 시연이며, `battleId`는 예측하기 어려운 접근 토큰 역할을 합니다. 계정 인증과 공개 인터넷 배포 설정은 포함하지 않았습니다.

## 검사

별도 데이터베이스를 지정해 서버를 실행한 다음 통합 검사를 실행합니다.

```sh
CRIMSON_DB_PATH=/tmp/crimson-tide-test.db dotnet run --project Server/CrimsonTide.Server.csproj
node Server/Tests/integration.mjs
```

통합 검사는 전투 시작과 턴 재시도, 다른 행동의 충돌, 승리 전 등록 거절, 승리 기록 등록·재전송, 랭킹 조회를 확인합니다. 검사 DB에는 테스트 기록이 생성되므로 실제 랭킹 DB와 분리하세요.
