namespace CrimsonTide.Server.Configuration;

/// <summary>전투·랭킹 저장소 설정. appsettings.json의 <c>BattleStore</c> 섹션과 바인딩한다.</summary>
public sealed class BattleStoreOptions
{
    public const string SectionName = "BattleStore";

    /// <summary>SQLite 파일 경로. 상대 경로는 콘텐츠 루트 기준으로 해석한다.</summary>
    public string DatabasePath { get; set; } = Path.Combine("data", "crimson-tide.db");
}
