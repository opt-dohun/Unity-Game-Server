namespace CrimsonTide.Server.Configuration;

/// <summary>전투·랭킹 저장소 설정. appsettings.json의 <c>BattleStore</c> 섹션과 바인딩한다.</summary>
public sealed class BattleStoreOptions
{
    public const string SectionName = "BattleStore";

    /// <summary>저장소 공급자. 개발 기본값은 sqlite이며 배포 환경은 mysql로 설정한다.</summary>
    public string Provider { get; set; } = "sqlite";

    /// <summary>SQLite 파일 경로. 상대 경로는 콘텐츠 루트 기준으로 해석한다.</summary>
    public string DatabasePath { get; set; } = Path.Combine("data", "crimson-tide.db");

    /// <summary>MySQL 연결 문자열은 코드/설정 파일에 저장하지 말고 secret 환경 변수로 주입한다.</summary>
    public string? MySqlConnectionString { get; set; }

    /// <summary>
    /// 턴 처리의 잠금 전략. <c>gate</c>(기본, 전역 락) 또는 <c>optimistic</c>(낙관적 락:
    /// version 컬럼으로 충돌을 감지해 재시도한다).
    /// </summary>
    public string LockMode { get; set; } = "gate";

    /// <summary>
    /// SQLite journal 모드. <c>wal</c>(기본) 또는 <c>delete</c>(SQLite 기본값).
    /// WAL은 읽기와 쓰기가 서로를 막지 않아 동시 writer 경합을 크게 줄인다.
    /// </summary>
    public string JournalMode { get; set; } = "wal";
}
