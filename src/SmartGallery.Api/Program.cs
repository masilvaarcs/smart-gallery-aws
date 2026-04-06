using Amazon.DynamoDBv2;
using Amazon.Rekognition;
using Amazon.S3;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using SmartGallery.Api.Config;
using SmartGallery.Api.Services;
using SmartGallery.Shared.DTOs;
using SmartGallery.Shared.Models;

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
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

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

    var form = await request.ReadFormAsync(ct);
    var arquivo = form.Files.GetFile("arquivo");

    if (arquivo is null || arquivo.Length == 0)
        return Results.BadRequest(new { erro = "Nenhum arquivo enviado. Use o campo 'arquivo'." });

    var titulo = form["titulo"].ToString();
    if (string.IsNullOrWhiteSpace(titulo))
        titulo = Path.GetFileNameWithoutExtension(arquivo.FileName);

    var descricao = form["descricao"].ToString();
    var tags = form["tags"].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
    var publica = !bool.TryParse(form["publica"], out var p) || p;

    // Validar tipo de arquivo
    var formatosPermitidos = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    var extensao = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
    if (!formatosPermitidos.Contains(extensao))
        return Results.BadRequest(new { erro = $"Formato não suportado: {extensao}. Use: {string.Join(", ", formatosPermitidos)}" });

    // Limite de 10MB
    if (arquivo.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { erro = "Arquivo excede o limite de 10MB." });

    // Upload para S3
    using var stream = arquivo.OpenReadStream();
    var s3Key = await s3.UploadAsync(stream, arquivo.FileName, arquivo.ContentType, ct);

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

    var lista = imagens.Select(img => new ImagemResumoResponse(
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
        img.UsuarioId,
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
    var (total, totalBytes) = await dynamoDb.ContarAsync(ct);
    var (imagens, _) = await dynamoDb.ListarAsync(1000, null, ct);

    var porFormato = imagens
        .GroupBy(i => i.Formato)
        .ToDictionary(g => g.Key, g => g.Count());

    var tagsPopulares = imagens
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

public partial class Program { }
