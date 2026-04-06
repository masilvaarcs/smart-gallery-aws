using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using SmartGallery.Api.Config;

namespace SmartGallery.Api.Services;

/// <summary>
/// Serviço para análise inteligente de imagens via Amazon Rekognition.
/// Detecta labels (objetos, cenas, conceitos) e retorna tags com score de confiança.
/// </summary>
public class RekognitionService
{
    private readonly IAmazonRekognition _rekognition;
    private readonly AwsConfig _config;
    private readonly ILogger<RekognitionService> _logger;

    /// <summary>Confiança mínima (%) para aceitar um label como tag.</summary>
    private const float ConfiancaMinima = 70f;

    /// <summary>Máximo de labels retornados pelo Rekognition.</summary>
    private const int MaxLabels = 15;

    public RekognitionService(IAmazonRekognition rekognition, AwsConfig config, ILogger<RekognitionService> logger)
    {
        _rekognition = rekognition;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Analisa uma imagem já armazenada no S3 e retorna tags geradas por IA.
    /// </summary>
    /// <param name="s3Key">Chave do objeto no S3.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Lista de tags (labels) detectadas com confiança >= 70%.</returns>
    public async Task<List<string>> AnalisarImagemAsync(string s3Key, CancellationToken ct)
    {
        try
        {
            var request = new DetectLabelsRequest
            {
                Image = new Image
                {
                    S3Object = new S3Object
                    {
                        Bucket = _config.S3Bucket,
                        Name = s3Key
                    }
                },
                MaxLabels = MaxLabels,
                MinConfidence = ConfiancaMinima
            };

            var response = await _rekognition.DetectLabelsAsync(request, ct);

            var tags = response.Labels
                .OrderByDescending(l => l.Confidence)
                .Select(l => l.Name.ToLowerInvariant())
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "Rekognition analisou {Key}: {Count} tags detectadas — [{Tags}]",
                s3Key, tags.Count, string.Join(", ", tags));

            return tags;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Rekognition falhou para {Key}. Upload continua sem tags de IA.", s3Key);
            return [];
        }
    }
}
