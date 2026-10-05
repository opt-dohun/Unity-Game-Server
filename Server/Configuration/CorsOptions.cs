namespace CrimsonTide.Server.Configuration;

/// <summary>브라우저 미리보기 클라이언트가 접근할 수 있는 오리진 목록.</summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
