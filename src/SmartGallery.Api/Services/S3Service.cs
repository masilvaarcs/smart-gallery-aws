using Amazon.S3;
using Amazon.S3.Model;
using SmartGallery.Api.Config;

namespace SmartGallery.Api.Services;

/// <summary>
/// Serviço para operações com Amazon S3 (upload, download, delete, URL assinada).
/// </summary>
public class S3Service
{
    private readonly IAmazonS3 _s3;
    private readonly AwsConfig _config;
    private readonly ILogger<S3Service> _logger;

    public S3Service(IAmazonS3 s3, AwsConfig config, ILogger<S3Service> logger)
    {
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Faz upload de uma imagem para o S3.
    /// </summary>
    public async Task<string> UploadAsync(Stream conteudo, string nomeArquivo, string contentType, CancellationToken ct)
    {
        var key = $"{_config.S3Prefixo}{Guid.NewGuid()}/{nomeArquivo}";

        var request = new PutObjectRequest
        {
            BucketName = _config.S3Bucket,
            Key = key,
            InputStream = conteudo,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
            AutoCloseStream = false   // stream gerenciado pelo chamador (reutilizado p/ ExtrairDimensoes)
        };

        await _s3.PutObjectAsync(request, ct);
        _logger.LogInformation("Upload S3: {Key} ({ContentType})", key, contentType);

        return key;
    }

    /// <summary>
    /// Gera URL assinada (pre-signed) para acesso temporário à imagem.
    /// </summary>
    public string GerarUrlAssinada(string key)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _config.S3Bucket,
            Key = key,
            Expires = DateTime.UtcNow.AddMinutes(_config.UrlAssinadaExpiracaoMinutos),
            Verb = HttpVerb.GET
        };

        return _s3.GetPreSignedURL(request);
    }

    /// <summary>
    /// Remove uma imagem do S3.
    /// </summary>
    public async Task DeletarAsync(string key, CancellationToken ct)
    {
        await _s3.DeleteObjectAsync(_config.S3Bucket, key, ct);
        _logger.LogInformation("Deletado S3: {Key}", key);
    }

    /// <summary>
    /// Verifica se o bucket existe; cria se necessário (dev local).
    /// </summary>
    public async Task GarantirBucketAsync(CancellationToken ct)
    {
        try
        {
            await _s3.EnsureBucketExistsAsync(_config.S3Bucket);
            _logger.LogInformation("Bucket S3 '{Bucket}' garantido.", _config.S3Bucket);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível verificar/criar o bucket S3 '{Bucket}'. Operações S3 podem falhar.", _config.S3Bucket);
        }
    }
}
