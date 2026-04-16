using Amazon.DynamoDBv2;
using Amazon.Rekognition;
using Amazon.S3;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using SmartGallery.Api.Config;
using SmartGallery.Api.Services;
using SmartGallery.Shared.DTOs;
using SmartGallery.Shared.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Executa como handler Lambda para API Gateway HTTP API em ambiente AWS.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// Configuração AWS
var awsConfig = builder.Configuration.GetSection(AwsConfig.Secao).Get<AwsConfig>() ?? new AwsConfig();
builder.Services.AddSingleton(awsConfig);

// AWS SDK — S3 e DynamoDB
if (awsConfig.UsarLocalStack)
{
    // LocalStack para desenvolvimento local (emula S3 e DynamoDB)
    var localStackConfig = new AmazonS3Config
    {
        ServiceURL = awsConfig.LocalStackUrl,
        ForcePathStyle = true,
        AuthenticationRegion = awsConfig.Regiao
    };
    builder.Services.AddSingleton<IAmazonS3>(_ =>
        new AmazonS3Client("test", "test", localStackConfig));

    var dynamoConfig = new AmazonDynamoDBConfig
    {
        ServiceURL = awsConfig.LocalStackUrl,
        AuthenticationRegion = awsConfig.Regiao
    };
    builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
        new AmazonDynamoDBClient("test", "test", dynamoConfig));
}
else
{
    // AWS real — usa credenciais do ambiente/perfil IAM
    builder.Services.AddAWSService<IAmazonS3>();
    builder.Services.AddAWSService<IAmazonDynamoDB>();
    builder.Services.AddAWSService<IAmazonRekognition>();
}

// Serviços da aplicação
builder.Services.AddSingleton<S3Service>();
builder.Services.AddSingleton<DynamoDbService>();
builder.Services.AddSingleton<RekognitionService>();

// CORS para MAUI
builder.Services.AddCors(options =>
{
    var origens = (awsConfig.CorsOrigensPermitidas ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    options.AddDefaultPolicy(policy =>
    {
        if (origens.Length == 0)
        {
            policy.WithOrigins("http://localhost:5123", "http://localhost:5050", "http://localhost:5221");
        }
        else
        {
            policy.WithOrigins(origens);
        }

        policy.WithMethods("GET", "POST", "DELETE", "OPTIONS")
              .WithHeaders("Content-Type", "Authorization");
    });
});

var app = builder.Build();

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { erro = "Erro interno. Tente novamente em instantes." });
    });
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

// Inicializar infraestrutura (criar bucket/tabela em dev)
using (var scope = app.Services.CreateScope())
{
    var s3 = scope.ServiceProvider.GetRequiredService<S3Service>();
    var dynamo = scope.ServiceProvider.GetRequiredService<DynamoDbService>();
    await s3.GarantirBucketAsync(CancellationToken.None);
    await dynamo.GarantirTabelaAsync(CancellationToken.None);
}

app.UseHttpsRedirection();
app.UseCors();

// ==========================================
// ENDPOINTS — Galeria de Imagens
// ==========================================

var api = app.MapGroup("/api/imagens").WithTags("Imagens");

// POST /api/imagens — Upload de imagem
api.MapPost("/", async (
    HttpRequest request,
    S3Service s3,
    DynamoDbService dynamoDb,
    RekognitionService rekognition,
    AwsConfig config,
    CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { erro = "Envie como multipart/form-data." });

    // Lê o formulário (ASP.NET Core usa UTF-8 por padrão; CorrigirEncoding garante robustez)
    var form = await request.ReadFormAsync(ct);
    var arquivo = form.Files.GetFile("arquivo");

    if (arquivo is null || arquivo.Length == 0)
        return Results.BadRequest(new { erro = "Nenhum arquivo enviado. Use o campo 'arquivo'." });

    // Garante decodificação UTF-8 correta em todos os campos de texto
    var titulo = CorrigirEncoding(form["titulo"].ToString());
    if (string.IsNullOrWhiteSpace(titulo))
        titulo = Path.GetFileNameWithoutExtension(arquivo.FileName);

    var descricao = CorrigirEncoding(form["descricao"].ToString());
    var tags = CorrigirEncoding(form["tags"].ToString())
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
    var publica = bool.TryParse(form["publica"], out var p) && p;

    // Validar tipo de arquivo
    var formatosPermitidos = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    var extensao = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
    if (!formatosPermitidos.Contains(extensao))
        return Results.BadRequest(new { erro = $"Formato não suportado: {extensao}. Use: {string.Join(", ", formatosPermitidos)}" });

    // Limite de 10MB
    if (arquivo.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { erro = "Arquivo excede o limite de 10MB." });

    // Lê stream uma única vez em buffer (reutilizado para S3 upload + extração de dimensões)
    using var ms = new MemoryStream();
    await arquivo.OpenReadStream().CopyToAsync(ms, ct);
    ms.Position = 0;
    var s3Key = await s3.UploadAsync(ms, arquivo.FileName, arquivo.ContentType, ct);

    // Extrai dimensões da imagem sem dependência externa
    int largura = 0, altura = 0;
    try
    {
        ms.Position = 0;
        (largura, altura) = ExtrairDimensoes(ms, extensao);
    }
    catch { /* dimensões não críticas — continua sem elas */ }

    // Análise de IA — Rekognition detecta labels (objetos, cenas, conceitos)
    var tagsIa = await rekognition.AnalisarImagemAsync(s3Key, ct);

    // Mescla tags manuais do usuário com tags geradas pela IA (sem duplicatas)
    var todasTags = tags
        .Concat(tagsIa)
        .Select(t => t.Trim().ToLowerInvariant())
        .Where(t => !string.IsNullOrEmpty(t))
        .Distinct()
        .ToList();

    // Salvar metadados no DynamoDB
    var metadata = new ImagemMetadata
    {
        Titulo = titulo,
        Descricao = descricao,
        Tags = todasTags,
        Formato = extensao.TrimStart('.'),
        TamanhoBytes = arquivo.Length,
        Largura = largura,
        Altura = altura,
        S3Key = s3Key,
        S3Bucket = config.S3Bucket,
        Publica = publica
    };

    await dynamoDb.SalvarAsync(metadata, ct);

    var url = s3.GerarUrlAssinada(s3Key);

    return Results.Created($"/api/imagens/{metadata.Id}", new UploadImagemResponse(
        metadata.Id,
        metadata.Titulo,
        url,
        metadata.Formato,
        metadata.TamanhoBytes,
        metadata.DataUpload,
        tagsIa
    ));
})
.DisableAntiforgery()
.WithName("UploadImagem")
.WithDescription("Faz upload de uma imagem para o S3 e salva metadados no DynamoDB.");

// GET /api/imagens — Listar imagens
api.MapGet("/", async (
    DynamoDbService dynamoDb,
    S3Service s3,
    int? limite,
    string? token,
    CancellationToken ct) =>
{
    var (imagens, proximo) = await dynamoDb.ListarAsync(limite ?? 20, token, ct);
    var imagensPublicas = imagens.Where(i => i.Publica).ToList();

    var lista = imagensPublicas.Select(img => new ImagemResumoResponse(
        img.Id,
        img.Titulo,
        img.Formato,
        img.TamanhoBytes,
        s3.GerarUrlAssinada(img.S3Key),
        img.DataUpload,
        img.Tags
    )).ToList();

    return Results.Ok(new ListagemImagensResponse(lista, lista.Count, proximo));
})
.WithName("ListarImagens")
.WithDescription("Lista imagens com paginação.");

// GET /api/imagens/{id} — Detalhes de uma imagem
api.MapGet("/{id}", async (
    string id,
    DynamoDbService dynamoDb,
    S3Service s3,
    CancellationToken ct) =>
{
    var img = await dynamoDb.BuscarPorIdAsync(id, ct);
    if (img is null)
        return Results.NotFound(new { erro = "Imagem não encontrada." });

    if (!img.Publica)
        return Results.NotFound(new { erro = "Imagem não encontrada." });

    return Results.Ok(new ImagemDetalheResponse(
        img.Id,
        img.Titulo,
        img.Descricao,
        img.Tags,
        img.Formato,
        img.TamanhoBytes,
        img.Largura,
        img.Altura,
        s3.GerarUrlAssinada(img.S3Key),
        s3.GerarUrlAssinada(img.S3Key), // thumbnail = mesma imagem por ora
        img.DataUpload,
        img.Publica
    ));
})
.WithName("DetalheImagem")
.WithDescription("Retorna detalhes completos de uma imagem com URL assinada.");

// DELETE /api/imagens/{id} — Deletar imagem
api.MapDelete("/{id}", async (
    string id,
    DynamoDbService dynamoDb,
    S3Service s3,
    CancellationToken ct) =>
{
    var img = await dynamoDb.BuscarPorIdAsync(id, ct);
    if (img is null)
        return Results.NotFound(new { erro = "Imagem não encontrada." });

    await s3.DeletarAsync(img.S3Key, ct);
    await dynamoDb.DeletarAsync(id, ct);

    return Results.Ok(new { mensagem = $"Imagem '{img.Titulo}' deletada com sucesso." });
})
.WithName("DeletarImagem")
.WithDescription("Remove a imagem do S3 e os metadados do DynamoDB.");

// GET /api/imagens/busca?tag=paisagem ou ?termo=rio
api.MapGet("/busca", async (
    DynamoDbService dynamoDb,
    S3Service s3,
    string? tag,
    string? termo,
    CancellationToken ct) =>
{
    List<ImagemMetadata> resultados;

    if (!string.IsNullOrWhiteSpace(tag))
        resultados = await dynamoDb.BuscarPorTagAsync(tag, ct);
    else if (!string.IsNullOrWhiteSpace(termo))
        resultados = await dynamoDb.BuscarPorTituloAsync(termo, ct);
    else
        return Results.BadRequest(new { erro = "Informe 'tag' ou 'termo' para busca." });

    resultados = resultados.Where(i => i.Publica).ToList();

    var lista = resultados.Select(img => new ImagemResumoResponse(
        img.Id,
        img.Titulo,
        img.Formato,
        img.TamanhoBytes,
        s3.GerarUrlAssinada(img.S3Key),
        img.DataUpload,
        img.Tags
    )).ToList();

    return Results.Ok(new ListagemImagensResponse(lista, lista.Count, null));
})
.WithName("BuscarImagens")
.WithDescription("Busca imagens por tag ou título.");

// GET /api/imagens/stats — Estatísticas da galeria
api.MapGet("/stats", async (DynamoDbService dynamoDb, CancellationToken ct) =>
{
    var (imagens, _) = await dynamoDb.ListarAsync(1000, null, ct);
    var imagensPublicas = imagens.Where(i => i.Publica).ToList();

    var total = imagensPublicas.Count;
    var totalBytes = imagensPublicas.Sum(i => i.TamanhoBytes);

    var porFormato = imagensPublicas
        .GroupBy(i => i.Formato)
        .ToDictionary(g => g.Key, g => g.Count());

    var tagsPopulares = imagensPublicas
        .SelectMany(i => i.Tags)
        .GroupBy(t => t)
        .OrderByDescending(g => g.Count())
        .Take(10)
        .Select(g => g.Key)
        .ToList();

    return Results.Ok(new GaleriaStatsResponse(total, totalBytes, porFormato, tagsPopulares));
})
.WithName("EstatisticasGaleria")
.WithDescription("Retorna estatísticas gerais da galeria (total, formatos, tags populares).");

// Health check simples
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    servico = "SmartGallery API",
    horario = DateTime.UtcNow,
    versao = "1.0.0"
})).WithName("HealthCheck");

app.Run();

// ---------------------------------------------------------------------------
// Helpers locais
// ---------------------------------------------------------------------------

/// <summary>
/// Detecta e corrige double-encoding Latin-1/UTF-8 que ocorre quando
/// o cliente envia texto UTF-8 e o receptor interpreta como ISO-8859-1.
/// Ex: "demonstraÃ§Ã£o" → "demonstração"
/// </summary>
static string CorrigirEncoding(string texto)
{
    if (string.IsNullOrEmpty(texto)) return texto;
    try
    {
        var latin1 = Encoding.GetEncoding("ISO-8859-1");
        var bytes = latin1.GetBytes(texto);
        // Verifica se o texto parece ter sido mal-decodificado:
        // sequências C3 xx são caracteres UTF-8 de 2 bytes
        bool pareceDouble = bytes.Length > 1 &&
            bytes.Take(bytes.Length - 1)
                 .Where((b, i) => b == 0xC3 && bytes[i + 1] >= 0x80 && bytes[i + 1] <= 0xBF)
                 .Any();
        return pareceDouble ? Encoding.UTF8.GetString(bytes) : texto;
    }
    catch
    {
        return texto;
    }
}

/// <summary>
/// Extrai largura e altura de JPEG ou PNG lendo apenas os cabeçalhos,
/// sem depender de bibliotecas externas.
/// </summary>
static (int largura, int altura) ExtrairDimensoes(Stream stream, string extensao)
{
    if (extensao is ".jpg" or ".jpeg")
    {
        // JPEG: percorre marcadores até encontrar SOF0/SOF2
        var reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0xD8FF) return (0, 0); // não é JPEG
        while (stream.Position < stream.Length - 8)
        {
            if (reader.ReadByte() != 0xFF) break;
            byte marker = reader.ReadByte();
            int segLen = (reader.ReadByte() << 8) | reader.ReadByte();
            // SOF0(0xC0), SOF1(0xC1), SOF2(0xC2) contêm dimensões
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                reader.ReadByte(); // precisão
                int h = (reader.ReadByte() << 8) | reader.ReadByte();
                int w = (reader.ReadByte() << 8) | reader.ReadByte();
                return (w, h);
            }
            stream.Seek(segLen - 2, SeekOrigin.Current);
        }
    }
    else if (extensao == ".png")
    {
        // PNG: 8 bytes assinatura + 4 len + "IHDR" + 4 width + 4 height
        stream.Seek(16, SeekOrigin.Begin);
        var b = new byte[8];
        if (stream.Read(b, 0, 8) == 8)
        {
            int w = (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
            int h = (b[4] << 24) | (b[5] << 16) | (b[6] << 8) | b[7];
            return (w, h);
        }
    }
    return (0, 0);
}

public partial class Program { }
