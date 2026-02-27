namespace ServiceIntegration.Core.Abstractions;

/// <summary>
/// Service đẩy document log lên Elasticsearch.
/// Không tự động gọi - caller chủ động qua <see cref="ElasticLogEntry"/>.
/// </summary>
public interface IElasticLogger
{
    /// <summary>
    /// Gửi <paramref name="entry"/> lên Elasticsearch.
    /// Level và message được set bởi InfoAsync/WarnAsync/ErrorAsync trước khi gọi hàm này.
    /// </summary>
    Task PostAsync(ElasticLogEntry entry, CancellationToken ct = default);
}
