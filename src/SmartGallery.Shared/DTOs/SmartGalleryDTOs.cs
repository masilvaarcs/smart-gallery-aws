namespace SmartGallery.Shared.DTOs;

/// <summary>Request — Upload com metadados (imagem vai como multipart).</summary>
public record UploadImagemRequest(
    string Titulo,
    string? Descricao = null,
    List<string>? Tags = null,
    bool Publica = true
);

/// <summary>Response — Resultado do upload.</summary>
public record UploadImagemResponse(
    string Id,
    string Titulo,
    string Url,
    string Formato,
    long TamanhoBytes,
    DateTime DataUpload,
    List<string> TagsIa
);

/// <summary>Response — Detalhes completos de uma imagem.</summary>
public record ImagemDetalheResponse(
    string Id,
    string Titulo,
    string Descricao,
    List<string> Tags,
    string Formato,
    long TamanhoBytes,
    int Largura,
    int Altura,
    string UrlAssinada,
    string UrlThumbnail,
    string UsuarioId,
    DateTime DataUpload,
    bool Publica
);

/// <summary>Response — Item da listagem (sem URL assinada pesada).</summary>
public record ImagemResumoResponse(
    string Id,
    string Titulo,
    string Formato,
    long TamanhoBytes,
    string UrlThumbnail,
    DateTime DataUpload,
    List<string> Tags
);

/// <summary>Response — Listagem paginada.</summary>
public record ListagemImagensResponse(
    List<ImagemResumoResponse> Imagens,
    int Total,
    string? ProximoToken
);

/// <summary>Request — Busca por tags ou título.</summary>
public record BuscaImagensRequest(
    string? Termo = null,
    List<string>? Tags = null,
    int Limite = 20,
    string? Token = null
);

/// <summary>Response — Estatísticas da galeria.</summary>
public record GaleriaStatsResponse(
    int TotalImagens,
    long TotalBytes,
    Dictionary<string, int> PorFormato,
    List<string> TagsPopulares
);
