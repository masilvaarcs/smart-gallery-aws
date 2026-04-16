using System.Text.Json.Serialization;

namespace SmartGallery.Shared.Models;

/// <summary>
/// Metadados de uma imagem armazenada no S3.
/// Mapeada para a tabela DynamoDB "SmartGallery-Imagens".
/// </summary>
public class ImagemMetadata
{
    /// <summary>Partition Key — UUID gerado no upload.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Título da imagem.</summary>
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Descrição opcional.</summary>
    public string Descricao { get; set; } = string.Empty;

    /// <summary>Tags para busca (ex: "paisagem,natureza,rio").</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Formato do arquivo (jpg, png, webp).</summary>
    public string Formato { get; set; } = string.Empty;

    /// <summary>Tamanho em bytes.</summary>
    public long TamanhoBytes { get; set; }

    /// <summary>Largura da imagem em pixels.</summary>
    public int Largura { get; set; }

    /// <summary>Altura da imagem em pixels.</summary>
    public int Altura { get; set; }

    /// <summary>Chave do objeto no S3 (ex: "imagens/uuid.jpg").</summary>
    public string S3Key { get; set; } = string.Empty;

    /// <summary>Nome do bucket S3.</summary>
    public string S3Bucket { get; set; } = string.Empty;

    /// <summary>ID do usuário que fez upload.</summary>
    public string UsuarioId { get; set; } = "anon-user";

    /// <summary>Data/hora do upload (UTC ISO 8601).</summary>
    public DateTime DataUpload { get; set; } = DateTime.UtcNow;

    /// <summary>Se a imagem é pública.</summary>
    public bool Publica { get; set; } = false;
}
