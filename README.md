# 붉은 파도 — 서버 판정 턴제 보스전

Unity와 웹 클라이언트가 같은 HTTP API를 사용하는 턴제 보스전입니다. 피해 계산·턴 진행·기록 저장을
모두 서버가 판정하고 클라이언트는 연출만 담당합니다. 게임 기능 자체보다 **서버 설계와 동시성 검증**에
초점을 둔 프로젝트로, 잠금 전략을 바꿔 가며 부하를 측정하고 결과를 근거와 함께 정리했습니다.

## 핵심 요약

**한 줄 소개.** .NET 10 + 개발 SQLite / 배포 MySQL(InnoDB)로 서버 권위 턴제 전투 API를 구현하고, SQLite에서 전역 락과 낙관적 락을 k6로 비교했습니다.

| 항목         | 내용                                                                                                          |
| ------------ | ------------------------------------------------------------------------------------------------------------- |
| 기술 스택    | C# · ASP.NET Core(.NET 10) · 개발 SQLite(WAL) / 배포 MySQL 8(InnoDB) · Unity 6 · 바닐라 JS · k6 · Docker      |
| 인증 및 보안 | `Idempotency-Key` 멱등 생성, 턴 단위 응답 재생, `version` CAS, SQLite/MySQL 일시 잠금 재시도, 트랜잭션 일관성 |
| 검증         | PCG32 난수 C↔C# 시퀀스 일치 검사 · HTTP 통신 유효성 검사 · k6 밴치마킹 테스트                                 |
| 측정         | 서버 내부 계측 + k6 지연 백분위, 부하 테스트 요청 유형 **단일 요청, 순차적 요청, 동시성 요청** 구분           |

### 밴치마킹 분석 내용

- **가상 유저 20 · 순차적 요청 처리** — 전역 락 486–562 전투/초(p95 10–12ms, 최대 25ms 이하) vs 낙관적 락 105–120 전투/초(p95 1.2–1.5ms이지만 p99 170ms·최대 7.3초). 낙관적 락은 p95만 좋아지고 p99와의 과도한 편차로 평균 응답시간이 악화된 것을 확인하였습니다.
- **가상 유저 20 · 동시성 요청 처리** — 중복 처리에 대한 예외 409를 정상으로 분류하여 검증(`unexpected_status=0`, checks 100%). 전역 락 21,000 요청/초(최대 40ms) vs 낙관적 락 4,352–5,056 요청/초(최대 2.4초) 임을 확인하였습니다.
- **가상 유저 1 · 단일 요청 처리(기준 데이터)** — 전역 락, 낙관적 락 모두 동일(평균 0.353 vs 0.354ms, 224.0 vs 222.6 전투/초). 경합이 없는 환경으로 전체 처리 소요에 발생하는 소요 시간을 예측하였습니다.
- **측정 근거** — 모든 수치는 `loadtest/results_remeasure/`의 실행별 JSON과 일치합니다. 통합 검사 실패·예상 밖 상태 코드가 있는 런은 실행기가 저장하지 않습니다.

## 디렉터리 구조

| 디렉터리           | 내용                                                                        |
| ------------------ | --------------------------------------------------------------------------- |
| `Server`           | ASP.NET Core 서버: 엔드포인트, 전투 규칙, 설정, 통합 검사                   |
| `Infra`            | SQLite 저장소(`BattleStore.cs`), 계측(`ServerMetrics.cs`), Docker·Terraform |
| `spec`             | OpenAPI 3 스펙과 클라이언트 생성용 `Makefile` 타깃                          |
| `Client/UnityGame` | Unity 6 프로젝트(씬·스크립트·스프라이트)와 macOS 빌드                       |
| `Client/Browser`   | 같은 API를 쓰는 웹 클라이언트(턴 전투·랭킹)                                 |
| `Server/LegacyC`   | 초기 C 프로토타입(PCG32 데모·리더보드). 현재 서버 코드는 쓰지 않습니다.     |

## 실행

.NET 10 SDK가 필요합니다.

```sh
dotnet run --project Server/CrimsonTide.Server.csproj
```

서버는 `127.0.0.1:8001`에서 듣고 `Server/data/crimson-tide.db`에 전투와 랭킹을 저장합니다. 개발 DB 경로는 `CRIMSON_DB_PATH` 환경 변수로 바꿀 수 있습니다. 이 서버를 켜야 전투를 시작할 수 있습니다.

### Docker/MySQL 배포

배포 기본 설정은 MySQL 8(InnoDB)이며, 시작 시 데이터베이스와 테이블을 준비합니다. MySQL 서버와 `game_database` 같은 스키마는 미리 생성하고, 해당 스키마에 테이블을 만들 권한이 있는 별도 앱 사용자를 제공하세요. 연결 문자열은 저장소에 넣지 말고 런타임 secret으로 전달합니다.

```sh
# 연결 문자열을 secret manager에서 환경 변수로 주입한 뒤 실행
docker build -t crimson-tide-server -f Infra/docker/server/Dockerfile .
docker run --rm -p 8080:8080 \\
  -e CRIMSON_MYSQL_CONNECTION_STRING="$CRIMSON_MYSQL_CONNECTION_STRING" \\
  crimson-tide-server
```

`CRIMSON_MYSQL_CONNECTION_STRING` 대신 .NET 표준 `ConnectionStrings__BattleStore`를 사용할 수도 있습니다. Docker 배포에서 MySQL 연결 문자열이 빠지면 서버는 시작에 실패합니다. SQLite 개발 DB는 MySQL로 자동 복사되지 않습니다. 이미 생성된 로컬 기록을 옮겨야 한다면 별도 데이터 이관 절차가 필요합니다.

Unity는 `Client/UnityGame/Assets/Scenes/BossBattle.unity`를 열어 Play를 누르거나 `Client/UnityGame/Builds/CrimsonTide-TurnBased.app`을 실행하세요. 브라우저 미리보기는 프로젝트 루트에서 다음 명령으로 열 수 있습니다.

```sh
python3 -m http.server 8000
```

주소는 `http://localhost:8000/`입니다. 조작은 공격 `1`/`J`, 방어 `2`/`K`입니다. 공격 피해는 1–20이고 방어는 보스 피해를 80% 줄입니다.

컨테이너로 실행하려면 `docker build -t crimson-tide-server -f Infra/docker/server/Dockerfile .` 후 `docker run --rm -p 8080:8080 crimson-tide-server`를 사용합니다. 이미지는 .NET 10 SDK/런타임 다단 빌드이고 저장 데이터는 `/data` 볼륨에 둡니다.

## 서버 판정과 중복 처리

1. `POST /api/battles` — 빈 JSON과 `Idempotency-Key` 헤더로 전투를 시작합니다. 같은 키로 재시도하면 같은 전투를 반환합니다.
2. `POST /api/battles/{battleId}/turns` — `{ "turn": 1, "action": "attack" }` 또는 `guard`를 전송합니다. 서버가 PCG32 난수로 피해량을 계산하고 양쪽 체력, 패턴, 종료 여부를 반환합니다.
3. 같은 `battleId + turn + action`을 다시 보내면 저장된 응답을 그대로 돌려줍니다. 이미 처리한 턴에 다른 행동을 보내거나 턴 순서를 건너뛰면 `409 Conflict`입니다.
4. 승리 후 `POST /api/battles/{battleId}/score`에 `{ "name": "기사" }`를 보냅니다. 턴 수와 체력은 서버의 전투 기록에서 가져옵니다. 한 전투의 기록은 한 번만 등록됩니다.
5. `GET /api/scores`는 적은 턴 수, 높은 남은 체력 순의 상위 20개를 반환합니다.

턴 판정·상태 변경·결과 저장은 현재 선택된 저장소의 한 트랜잭션에서 함께 처리합니다. 개발에서는 SQLite WAL 파일, 배포에서는 MySQL InnoDB를 사용하며, SQLite 개발 데이터는 MySQL로 자동 복사되지 않습니다. 서버를 재시작해도 선택된 DB의 전투 상태와 처리한 턴 응답이 남습니다. `battleId`는 예측하기 어려운 접근 토큰 역할을 하지만, 계정 인증과 공개 인터넷 배포 보안 설정은 별도 작업입니다.

## 검사

**규칙 단위 검사**는 C 프로토타입과 C# 구현이 같은 난수 시퀀스를 내는지 확인합니다. 피해량이 서버
판정의 기준값이므로 두 구현이 어긋나면 나머지 검증이 무의미해지기 때문입니다.

```sh
dotnet run --project Server/Tests/RulesCheck.csproj
```

**HTTP 통합 검사**는 별도 데이터베이스로 서버를 띄운 뒤 실행합니다. 멱등 생성, 턴 재시도 응답 일치,
다른 행동·순서 건너뛰기의 `409`, 승리 전 기록 거절, 기록 등록과 재전송, 랭킹 반영까지 단언 17개로
확인합니다.

```sh
CRIMSON_DB_PATH=/tmp/crimson-tide-test.db dotnet run --project Server/CrimsonTide.Server.csproj
node Server/Tests/integration.mjs
```

검사 DB에는 테스트 기록이 생성되므로 실제 랭킹 DB와 분리하세요.

## 전역 락 과 낙관적 락의 지연 시간 차이 분석

전역 세마포어로 턴 처리를 직렬화하는 구현과, `version` 조건부 갱신(CAS)으로 충돌을 감지해 재시도하는
구현을 같은 부하로 비교했습니다. 두 구현은 설정만 바꿔 전환하므로 코드 경로 외 조건은 동일합니다.

```jsonc
// appsettings.json 또는 환경 변수로 전환
"BattleStore": { "LockMode": "gate", "JournalMode": "wal" }   // 또는 "optimistic"
```

### 측정 설계

**가상 유저 20 · 순차적 요청 처리** — 전역 락 486–562 전투/초(p95 10–12ms, 최대 25ms 이하) vs 낙관적 락 105–120 전투/초(p95 1.2–1.5ms이지만 p99 170ms·최대 7.3초). 낙관적 락은 p95만 좋아지고 p99와의 과도한 편차로 평균 응답시간이 악화된 것을 확인하였습니다.

- **가상 유저 20 · 동시성 요청 처리** — 중복 처리에 대한 예외 409를 정상으로 분류하여 검증(`unexpected_status=0`, checks 100%). 전역 락 21,000 요청/초(최대 40ms) vs 낙관적 락 4,352–5,056 요청/초(최대 2.4초) 임을 확인하였습니다.
- **가상 유저 1 · 단일 요청 처리(기준 데이터)** — 전역 락, 낙관적 락 모두 동일(평균 0.353 vs 0.354ms, 224.0 vs 222.6 전투/초). 경합이 없는 환경으로 전체 처리 소요에 발생하는 소요 시간을 예측하였습니다.
- **측정 근거** — 모든 수치는 `loadtest/results_remeasure/`의 실행별 JSON과 일치합니다. 통합 검사 실패·예상 밖 상태 코드가 있는 런은 실행기가 저장하지 않습니다.

- **부하 유형** — 가상 유저 1 · 단일 요청 처리(기준 데이터), 가상 유저 20 · 순차적 요청 처리, 가상 유저 20 · 동시성 요청 처리).
- **측정 환경 통일** — 프레임워크의 요청 단위 로깅을 끄고, 기동 로그에서 실제 journal 모드가 `wal`인지 확인하며, 런마다 새 DB 파일을 씁니다. 구성 순서를 뒤집은 반복 런도 함께 기록했습니다.
- **무효 턴 수집** — 통합 검사가 실패하거나 checks 100% 미만, 예상 밖 상태 코드가 1건이라도 있으면 실행기가 결과를 저장하지 않고 중단합니다.
- **지표 수집 기준** — 서버가 임계 구역 진입 대기(`lock_wait_*`), SQLite 트랜잭션 소요(`db_*`), 재시도(`db_busy_retry`, `conflict_retry`)를 마이크로초로 수집하고 `/api/server/metrics`로 노출합니다.

### 결과

지연은 ms, 처리량은 iteration/s입니다. 독립 전투 프로필에서 iteration은 전투 완료 1회, 같은 전투 프로필에서는 요청 1건입니다.

| 부하 / 런               | 잠금 경로  |   평균 |    p95 |     p99 |    최대 |   처리량 | busy 재시도 |
| ----------------------- | ---------- | -----: | -----: | ------: | ------: | -------: | ----------: |
| VU 1 · 독립             | gate       |  0.353 |  0.732 |   1.064 |    16.0 |    224.0 |           0 |
| VU 1 · 독립             | optimistic |  0.354 |  0.735 |   1.094 |    16.0 |    222.6 |           0 |
| VU 20 · 독립 · 런1      | gate       |  3.657 | 12.300 |  15.643 |    24.8 |    486.3 |           0 |
| VU 20 · 독립 · 런1      | optimistic | 16.743 |  1.510 | 169.980 | 7,314.4 |    105.0 |         339 |
| VU 20 · 독립 · 런2      | gate       |  3.165 | 10.150 |  11.424 |    17.4 |    562.3 |           0 |
| VU 20 · 독립 · 런2      | optimistic | 15.049 |  1.169 |   6.044 | 8,787.4 |    119.5 |         388 |
| VU 20 · 같은 전투 · 런1 | gate       |  0.890 |  1.585 |   2.285 |    40.0 | 20,993.4 |           0 |
| VU 20 · 같은 전투 · 런1 | optimistic |  4.516 |  0.671 |  91.876 | 2,425.9 |  4,352.0 |          59 |
| VU 20 · 같은 전투 · 런2 | gate       |  0.867 |  1.571 |   2.159 |    18.9 | 21,563.4 |           0 |
| VU 20 · 같은 전투 · 런2 | optimistic |  3.892 |  0.617 |  90.238 | 1,823.1 |  5,056.1 |          52 |

상세 JSON·서버 로그·k6 요약은 `loadtest/results_remeasure/` 아래 프로필별 폴더에 보존했고,
이전 측정 파일은 덮어쓰지 않았습니다. 차트는 같은 폴더의 JSON에서 생성합니다.

#### VU 20 · 독립 전투

![VU 20 독립 전투 지연 백분위수](loadtest/charts/independent-vu20/latency_percentiles.svg)

![VU 20 독립 전투 처리량](loadtest/charts/independent-vu20/throughput.svg)

전역 락은 지연 분포가 평평하고(평균 3.2–3.7ms, 최대 25ms 이하) 처리량이 486–562 전투/초입니다.
낙관적 락은 p95가 1.2–1.5ms로 낮지만 평균이 15–17ms로 커지고 p99 170ms·최대 7.3–8.8초의 긴 꼬리가
생기며 처리량이 105–120 전투/초로 줄었습니다. 서버 계측이 원인을 보여 줍니다. 전역 락은 임계 구역
자체가 평균 0.18ms(p95 0.25ms)로 짧아 대기(`lock_wait_turn` 평균 3.28ms)가 전체 지연의 대부분을
차지하는 반면, 낙관적 락은 대기가 0인 대신 SQLite 쓰기 경합이 `db_busy_retry` 339–388회로 나타나며
건당 약 1.15초를 소진합니다.

![VU 20 독립 전투 지연 분해](loadtest/charts/independent-vu20/latency_breakdown.svg)

#### VU 20 · 같은 전투 동시 턴

![VU 20 같은 전투 지연 백분위수](loadtest/charts/shared-vu20/latency_percentiles.svg)

![VU 20 같은 전투 처리량](loadtest/charts/shared-vu20/throughput.svg)

같은 턴에 요청이 몰리면 규약대로 한 건만 처리되고 나머지는 `409`가 됩니다(런1 gate 630,322건 중
314,411건, optimistic 131,949건 중 63,499건). 예상 밖 상태 코드는 0건이고 checks는 100%였습니다.
전역 락이 21,000 요청/초·최대 40ms로 더 빠르고 안정적이었고, 낙관적 락은 4,352–5,056 요청/초에
최대 2.4초의 꼬리를 보였습니다.

#### VU 1 · 독립 전투 (대조군)

![VU 1 독립 전투 처리량](loadtest/charts/independent-vu1/throughput.svg)

경합이 없는 조건에서는 두 경로가 사실상 같았습니다(평균 0.353 vs 0.354ms, 224.0 vs 222.6 전투/초).
전역 락의 대기 시간도 0으로 측정되어, 게이트가 비용을 만드는 조건은 동시성이라는 점이 확인됩니다.

#### 참고: 왜 WAL에서 비교했는가

비교의 출발점은 전역 락 자체가 아니었습니다. 기본 journal(DELETE) 모드에서 같은 전역 락을 측정하면
평균 168.8ms 중 160.2ms가 락 대기였는데, 이는 대기 시간이 `임계 구역 길이 × 대기 인원`이기 때문입니다.
SQLite 기본 journal은 쓰기마다 파일을 잠그고 fsync하므로 임계 구역이 p95 13.67ms까지 커져 있었고,
WAL로 바꾸자 0.23ms(약 59배 축소)로 줄면서 처리량이 10.6 → 510.9 전투/초(약 48배), p95가
187.8 → 11.0ms로 개선됐습니다. 그래서 이후 비교는 모두 WAL을 전제로 합니다(원본: `loadtest/results/`).

![WAL 적용 전후 지연 분해](loadtest/charts/wal-baseline/latency_breakdown.svg)

### 해석

1. **VU 1에서는 차이가 없었습니다.** 경합이 없으면 전역 락을 제거해 얻을 것이 없습니다.
2. **VU 20 독립 전투에서는 전역 락이 두 런 모두 우세했습니다.** 낙관적 락은 p95만 좋아 보이고 평균·p99·최대·처리량은 모두 나빠졌습니다. 평균 15–17ms와 p95 1.2–1.5ms를 함께 보면 긴 꼬리가 평균을 끌어올린 결과이므로, **p95 하나만 인용해 "8배 빨라졌다"고 결론내리면 안 됩니다.**
3. **같은 전투 프로필의 절반 가까운 요청은 의도된 409였습니다.** 이 처리량은 전체 전투 성능이 아니라 단일 턴에 몰린 중복 요청의 처리율입니다.
4. **현재 `optimistic` 경로는 엄밀한 의미의 "DB 내부 낙관적 경쟁"이 아닙니다.** 구현은 낙관적 요청에서도 `BeginTransaction(deferred: false)`로 즉시 쓰기 트랜잭션을 열어 SQLite writer 권한을 읽기보다 먼저 확보합니다. writer는 DB 안에서 직렬 대기하고, 측정된 `db_busy_retry`가 그 경합을 보여 줍니다. `version` CAS 재시도(`conflict_retry`)는 이 측정에서 발생하지 않았습니다.
5. **판정.** 현재 코드와 SQLite 파일 하나를 유지한다면 `gate` + WAL이 높은 동시 쓰기 처리량에서 유리했습니다. 다만 이 결과는 로컬 한 머신의 짧은 실행(1–2회 반복)이므로 일반화된 성능 보증이 아니라 이 환경에서의 측정입니다.

### MySQL(InnoDB) 구현 범위

배포 provider는 MySQL로 설정되고 `optimistic` 경로를 사용합니다. 턴 저장은 InnoDB 트랜잭션 안에서 `version` 조건부 UPDATE와 `(battle_id, turn)` 고유 키를 함께 사용합니다. 서로 다른 전투는 서로 다른 행/키를 갱신하고, 동일 전투의 중복 턴은 고유 키 충돌 후 새 시도에서 저장된 결과를 읽어 멱등 처리합니다. MySQL의 deadlock(1213)·lock-wait timeout(1205)은 제한된 지수 백오프로 재시도합니다. 이는 실제 MySQL 서버에서 아직 부하 측정하지 않았으므로 처리량 주장은 하지 않습니다. InnoDB는 행 단위 잠금과 트랜잭션을 지원하지만 동일 행 경합/데드락을 없애지는 않습니다.

### 재현

```sh
# 프로필별 측정(같은 폴더를 덮어쓰지 않도록 새 RESULTS_DIR을 지정)
RESULTS_DIR=loadtest/results_remeasure/20261005_single_vu1 VUS=1  DURATION=30s WORKLOAD=independent CONFIGS=gate:wal,optimistic:wal node loadtest/run_comparison.mjs
RESULTS_DIR=loadtest/results_remeasure/20261005_multi_vu20  VUS=20 DURATION=30s WORKLOAD=independent CONFIGS=gate:wal,optimistic:wal node loadtest/run_comparison.mjs
RESULTS_DIR=loadtest/results_remeasure/20261005_shared_vu20 VUS=20 DURATION=30s WORKLOAD=shared      CONFIGS=gate:wal,optimistic:wal node loadtest/run_comparison.mjs

# 차트 재생성(측정 폴더 → 차트 폴더)
RESULTS_DIR=loadtest/results_remeasure/20261005_multi_vu20 CHARTS_DIR=loadtest/charts/independent-vu20 node loadtest/make_charts.mjs
```

`WORKLOAD=shared`는 의도한 409를 허용하고 다른 HTTP 상태를 실패로 셉니다. `K6_BIN`으로 k6 실행 파일을,
`K6_RAW=1`로 k6 원시 덤프(실행당 수백 MB)를 켤 수 있습니다.

## 한계와 다음 단계

- 반복이 1–2회인 로컬 측정입니다. VU 규모를 4/8/16/32로 확장하고 여러 머신에서 반복해야 곡선으로 말할 수 있습니다.
- SQLite `optimistic` 경로는 `BEGIN IMMEDIATE` + 재시도 구조라 SQLite 전용 실험 결과를 MySQL의 version CAS 경로에 그대로 적용하면 안 됩니다.
- MySQL(InnoDB) 저장소와 설정은 연결했지만 이 환경에는 MySQL 서버/Docker가 없어 실제 MySQL 통합·동시성 검증은 아직 수행하지 못했습니다. 배포 전에 MySQL 8에서 마이그레이션, 동일 전투 경합, deadlock/timeout 재시도와 기존 API 검사를 실행해야 합니다.
