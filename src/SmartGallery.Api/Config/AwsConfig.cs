namespace SmartGallery.Api.Config;

/// <summary>
/// Configurações AWS carregadas do appsettings.json.
/// </summary>
public class AwsConfig
{
    public const string Secao = "AWS";

    /// <summary>Região AWS (ex: us-east-1).</summary>
    public string Regiao { get; set; } = "us-east-1";

    /// <summary>Nome do bucket S3 para imagens.</summary>
    public string S3Bucket { get; set; } = "smart-gallery-imagens";

    /// <summary>Prefixo das chaves no S3.</summary>
    public string S3Prefixo { get; set; } = "imagens/";

    /// <summary>Prefixo para thumbnails.</summary>
    public string S3PrefixoThumbnail { get; set; } = "thumbnails/";

    /// <summary>Nome da tabela DynamoDB.</summary>
    public string DynamoDbTabela { get; set; } = "SmartGallery-Imagens";

    /// <summary>Tempo de expiração da URL assinada (minutos).</summary>
    public int UrlAssinadaExpiracaoMinutos { get; set; } = 60;

    /// <summary>Se deve usar LocalStack para desenvolvimento local.</summary>
    public bool UsarLocalStack { get; set; } = true;

    /// <summary>URL do LocalStack (desenvolvimento local).</summary>
    public string LocalStackUrl { get; set; } = "http://localhost:4566";
}
