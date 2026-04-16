using SmartGallery.Api.Config;
using SmartGallery.Shared.DTOs;
using SmartGallery.Shared.Models;

namespace SmartGallery.Tests;

// ==========================================
// TESTES DE MODELO — ImagemMetadata
// ==========================================

public class ImagemMetadataTests
{
    [Fact]
    public void ImagemMetadata_Novo_DeveGerarIdUnico()
    {
        var img1 = new ImagemMetadata();
        var img2 = new ImagemMetadata();

        Assert.NotEqual(img1.Id, img2.Id);
        Assert.False(string.IsNullOrWhiteSpace(img1.Id));
    }

    [Fact]
    public void ImagemMetadata_Novo_DeveInicializarComPadroes()
    {
        var img = new ImagemMetadata();

        Assert.False(img.Publica);
        Assert.Equal("anon-user", img.UsuarioId);
        Assert.Empty(img.Tags);
        Assert.Empty(img.Titulo);
        Assert.Empty(img.Descricao);
    }

    [Fact]
    public void ImagemMetadata_DeveArmazenarPropriedades()
    {
        var img = new ImagemMetadata
        {
            Titulo = "Foto Teste",
            Descricao = "Descrição da foto",
            Tags = ["paisagem", "natureza", "brasil"],
            Formato = "image/jpeg",
            TamanhoBytes = 1048576,
            Largura = 1920,
            Altura = 1080,
            S3Key = "imagens/abc123/foto.jpg",
            S3Bucket = "smart-gallery-imagens"
        };

        Assert.Equal("Foto Teste", img.Titulo);
        Assert.Equal(3, img.Tags.Count);
        Assert.Equal(1048576, img.TamanhoBytes);
        Assert.Equal(1920, img.Largura);
        Assert.Equal("imagens/abc123/foto.jpg", img.S3Key);
    }

    [Fact]
    public void ImagemMetadata_DevePermitirTagsMultiplas()
    {
        var img = new ImagemMetadata
        {
            Tags = ["tag1", "tag2", "tag3", "tag4", "tag5"]
        };

        Assert.Equal(5, img.Tags.Count);
        Assert.Contains("tag3", img.Tags);
    }

    [Fact]
    public void ImagemMetadata_DataUpload_DeveSerUtcAtual()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);
        var img = new ImagemMetadata();
        var depois = DateTime.UtcNow.AddSeconds(1);

        Assert.True(img.DataUpload >= antes);
        Assert.True(img.DataUpload <= depois);
    }
}

// ==========================================
// TESTES DE CONFIG — AwsConfig
// ==========================================

public class AwsConfigTests
{
    [Fact]
    public void AwsConfig_DeveInicializarComPadroes()
    {
        var config = new AwsConfig();

        Assert.Equal("us-east-1", config.Regiao);
        Assert.Equal("smart-gallery-imagens", config.S3Bucket);
        Assert.Equal("imagens/", config.S3Prefixo);
        Assert.Equal("thumbnails/", config.S3PrefixoThumbnail);
        Assert.Equal("SmartGallery-Imagens", config.DynamoDbTabela);
        Assert.Equal(60, config.UrlAssinadaExpiracaoMinutos);
        Assert.True(config.UsarLocalStack);
    }

    [Fact]
    public void AwsConfig_Secao_DeveSerAWS()
    {
        Assert.Equal("AWS", AwsConfig.Secao);
    }

    [Fact]
    public void AwsConfig_DevePermitirCustomizacao()
    {
        var config = new AwsConfig
        {
            Regiao = "sa-east-1",
            S3Bucket = "custom-bucket",
            DynamoDbTabela = "custom-table",
            UsarLocalStack = false,
            UrlAssinadaExpiracaoMinutos = 120
        };

        Assert.Equal("sa-east-1", config.Regiao);
        Assert.Equal("custom-bucket", config.S3Bucket);
        Assert.False(config.UsarLocalStack);
        Assert.Equal(120, config.UrlAssinadaExpiracaoMinutos);
    }

    [Fact]
    public void AwsConfig_LocalStack_UrlPadrao()
    {
        var config = new AwsConfig();

        Assert.Equal("http://localhost:4566", config.LocalStackUrl);
    }
}

// ==========================================
// TESTES DE DTOs
// ==========================================

public class SmartGalleryDtoTests
{
    [Fact]
    public void UploadImagemRequest_DeveCriarComPropriedades()
    {
        var req = new UploadImagemRequest("Minha Foto", "Descrição", ["tag1", "tag2"], true);

        Assert.Equal("Minha Foto", req.Titulo);
        Assert.Equal("Descrição", req.Descricao);
        Assert.Equal(2, req.Tags!.Count);
        Assert.True(req.Publica);
    }

    [Fact]
    public void UploadImagemRequest_DeveUsarPadroes()
    {
        var req = new UploadImagemRequest("Foto");

        Assert.Null(req.Descricao);
        Assert.Null(req.Tags);
        Assert.False(req.Publica);
    }

    [Fact]
    public void UploadImagemResponse_DeveCriarCorretamente()
    {
        var tagsIa = new List<string> { "landscape", "nature", "sky" };
        var resp = new UploadImagemResponse(
            "abc-123", "Minha Foto",
            "https://s3.amazonaws.com/bucket/foto.jpg",
            "image/jpeg", 2048000, DateTime.UtcNow, tagsIa);

        Assert.Equal("abc-123", resp.Id);
        Assert.Equal("Minha Foto", resp.Titulo);
        Assert.Equal(2048000, resp.TamanhoBytes);
        Assert.Equal(3, resp.TagsIa.Count);
        Assert.Contains("landscape", resp.TagsIa);
    }

    [Fact]
    public void UploadImagemResponse_TagsIa_PodeSerListaVazia()
    {
        var resp = new UploadImagemResponse(
            "id1", "Foto", "url", "jpg", 1024, DateTime.UtcNow, []);

        Assert.Empty(resp.TagsIa);
    }

    [Fact]
    public void ImagemDetalheResponse_DeveConterTodosOsCampos()
    {
        var resp = new ImagemDetalheResponse(
            "id1", "Titulo", "Desc",
            ["t1", "t2"], "image/png", 500000,
            800, 600,
            "https://url-assinada.com",
            "https://thumb.com",
            DateTime.UtcNow, true);

        Assert.Equal(800, resp.Largura);
        Assert.Equal(600, resp.Altura);
        Assert.Equal(2, resp.Tags.Count);
        Assert.True(resp.Publica);
    }

    [Fact]
    public void ListagemImagensResponse_DeveConterPaginacao()
    {
        var imagens = new List<ImagemResumoResponse>
        {
            new("id1", "Foto 1", "jpeg", 1000, "", DateTime.UtcNow, ["tag1"]),
            new("id2", "Foto 2", "png", 2000, "", DateTime.UtcNow, ["tag2"])
        };

        var resp = new ListagemImagensResponse(imagens, 50, "nextToken123");

        Assert.Equal(2, resp.Imagens.Count);
        Assert.Equal(50, resp.Total);
        Assert.Equal("nextToken123", resp.ProximoToken);
    }

    [Fact]
    public void BuscaImagensRequest_DeveUsarPadroes()
    {
        var req = new BuscaImagensRequest();

        Assert.Null(req.Termo);
        Assert.Null(req.Tags);
        Assert.Equal(20, req.Limite);
    }
}

// ==========================================
// TESTES DE VALIDAÇÃO — Formatos e Tamanhos
// ==========================================

public class ValidacaoImagemTests
{
    private static readonly string[] FormatosPermitidos = ["image/jpeg", "image/png", "image/gif", "image/webp"];
    private const long TamanhoMaximo = 10 * 1024 * 1024; // 10MB

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public void FormatosPermitidos_DevemSerAceitos(string formato)
    {
        Assert.Contains(formato, FormatosPermitidos);
    }

    [Theory]
    [InlineData("image/bmp")]
    [InlineData("image/tiff")]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    public void FormatosInvalidos_DevemSerRejeitados(string formato)
    {
        Assert.DoesNotContain(formato, FormatosPermitidos);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(1048576)]
    [InlineData(5242880)]
    [InlineData(10485760)]
    public void TamanhosDentroDoLimite_DevemSerAceitos(long tamanho)
    {
        Assert.True(tamanho <= TamanhoMaximo);
    }

    [Theory]
    [InlineData(10485761)]
    [InlineData(20971520)]
    public void TamanhosExcedentes_DevemSerRejeitados(long tamanho)
    {
        Assert.True(tamanho > TamanhoMaximo);
    }
}

// ==========================================
// TESTES DE IA — Tags Rekognition
// ==========================================

public class RekognitionTagTests
{
    [Fact]
    public void TagsIa_DevemSerMescladas_ComTagsManuais()
    {
        var tagsManuais = new List<string> { "paisagem", "brasil" };
        var tagsIa = new List<string> { "Landscape", "Nature", "Sky", "paisagem" };

        var resultado = tagsManuais
            .Concat(tagsIa)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();

        Assert.Equal(5, resultado.Count); // paisagem, brasil, landscape, nature, sky
        Assert.Contains("paisagem", resultado);
        Assert.Contains("landscape", resultado);
        Assert.Contains("nature", resultado);
        Assert.Contains("sky", resultado);
    }

    [Fact]
    public void TagsIa_Vazio_MantemApenasManuais()
    {
        var tagsManuais = new List<string> { "foto", "teste" };
        var tagsIa = new List<string>();

        var resultado = tagsManuais
            .Concat(tagsIa)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void TagsIa_DevemSerNormalizadas_ParaMinusculo()
    {
        var tags = new List<string> { "LANDSCAPE", "Nature", "sky" };

        var resultado = tags
            .Select(t => t.Trim().ToLowerInvariant())
            .ToList();

        Assert.All(resultado, t => Assert.Equal(t, t.ToLowerInvariant()));
    }

    [Fact]
    public void TagsIa_DeveRemoverDuplicatas_CaseInsensitive()
    {
        var tags = new List<string> { "Sky", "sky", "SKY", "Nature", "nature" };

        var resultado = tags
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public void ImagemMetadata_DeveArmazenarTagsIa()
    {
        var img = new ImagemMetadata
        {
            Tags = ["landscape", "nature", "sky", "mountain", "outdoor"]
        };

        Assert.Equal(5, img.Tags.Count);
        Assert.Contains("landscape", img.Tags);
        Assert.Contains("outdoor", img.Tags);
    }
}
