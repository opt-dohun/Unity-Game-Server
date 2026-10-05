namespace CrimsonTide.Server.Configuration;

/// <summary>HTTP 바인딩 설정. appsettings.json의 <c>Server</c> 섹션과 바인딩한다.</summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    /// <summary>Kestrel 바인딩 URL 목록(';'로 구분). 로컬 기본값은 8001이다.</summary>
    public string Urls { get; set; } = "http://127.0.0.1:8001";
}
