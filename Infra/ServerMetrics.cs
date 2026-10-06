using System.Collections.Concurrent;

namespace CrimsonTide.Infra;

/// <summary>단일 메트릭의 통계 요약.</summary>
/// <param name="Count">표본 수</param>
/// <param name="MeanMs">평균 (ms)</param>
/// <param name="P50Ms">중앙값 (ms)</param>
/// <param name="P95Ms">p95 (ms)</param>
/// <param name="P99Ms">p99 (ms)</param>
/// <param name="MaxMs">최대 (ms)</param>
public sealed record MetricStats(long Count, double MeanMs, double P50Ms, double P95Ms, double P99Ms, double MaxMs);

/// <summary>
/// 인메모리 계측 수집기. 락 획득 대기 시간과 DB 트랜잭션별 소모 시간을
/// 미초 해상도로 샘플링해 /api/server/metrics 엔드포인트에서 스냅샷으로 노출한다.
/// 부하 테스트 구간(수십 초)에서 사용하는 규모에 적합하다.
/// </summary>
public sealed class ServerMetrics
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<long>> _samples = new(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="micros"/> 미초의 표본을 메트릭에 추가한다.
    /// <paramref name="name"/> 예: lock_wait, db_turn, conflict_retry
    /// </summary>
    public void Record(string name, double micros)
    {
        if (micros < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(micros));
        }

        _samples.GetOrAdd(name, static _ => new ConcurrentQueue<long>()).Enqueue((long)micros);
    }

    /// <summary>모든 표본을 지운다. 부하 테스트 구간을 프로세스 기동 이후와 분리할 때 사용한다.</summary>
    public void Clear()
    {
        _samples.Clear();
    }

    /// <summary>현재까지 수집한 전 메트릭의 통계 스냅샷을 반환한다.</summary>
    public IReadOnlyDictionary<string, MetricStats> Snapshot()
    {
        var result = new Dictionary<string, MetricStats>(StringComparer.Ordinal);
        foreach (var (name, queue) in _samples)
        {
            long[] samples = queue.ToArray();
            if (samples.Length == 0)
            {
                continue;
            }

            Array.Sort(samples);
            double sum = 0;
            foreach (long s in samples)
            {
                sum += s;
            }

            result[name] = new MetricStats(
                samples.Length,
                sum / samples.Length / 1_000.0,
                Percentile(samples, 50),
                Percentile(samples, 95),
                Percentile(samples, 99),
                samples[^1] / 1_000.0);
        }

        return result;
    }

    /// <summary>정렬된 배열에서 선형 보간 백분위수를 ms로 반환한다.</summary>
    private static double Percentile(long[] sorted, double p)
    {
        if (sorted.Length == 1)
        {
            return sorted[0] / 1_000.0;
        }

        double index = (sorted.Length - 1) * p / 100.0;
        int lo = (int)Math.Floor(index);
        int hi = (int)Math.Ceiling(index);
        double frac = index - lo;
        return (sorted[lo] + (sorted[hi] - sorted[lo]) * frac) / 1_000.0;
    }
}
