using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using SmartGallery.Api.Config;
using SmartGallery.Shared.Models;

namespace SmartGallery.Api.Services;

/// <summary>
/// Serviço para operações com Amazon DynamoDB (CRUD de metadados de imagens).
/// </summary>
public class DynamoDbService
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly AwsConfig _config;
    private readonly ILogger<DynamoDbService> _logger;

    private string Tabela => _config.DynamoDbTabela;

    public DynamoDbService(IAmazonDynamoDB dynamoDb, AwsConfig config, ILogger<DynamoDbService> logger)
    {
        _dynamoDb = dynamoDb;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Salva metadados de uma imagem no DynamoDB.
    /// </summary>
    public async Task SalvarAsync(ImagemMetadata imagem, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(imagem.Id),
            ["Titulo"] = new(imagem.Titulo),
            ["Descricao"] = new(imagem.Descricao),
            ["Tags"] = new(imagem.Tags),
            ["Formato"] = new(imagem.Formato),
            ["TamanhoBytes"] = new() { N = imagem.TamanhoBytes.ToString() },
            ["Largura"] = new() { N = imagem.Largura.ToString() },
            ["Altura"] = new() { N = imagem.Altura.ToString() },
            ["S3Key"] = new(imagem.S3Key),
            ["S3Bucket"] = new(imagem.S3Bucket),
            ["UsuarioId"] = new(imagem.UsuarioId),
            ["DataUpload"] = new(imagem.DataUpload.ToString("o")),
            ["Publica"] = new() { BOOL = imagem.Publica }
        };

        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = Tabela,
            Item = item
        }, ct);

        _logger.LogInformation("DynamoDB PutItem: {Id} — {Titulo}", imagem.Id, imagem.Titulo);
    }

    /// <summary>
    /// Busca uma imagem por ID.
    /// </summary>
    public async Task<ImagemMetadata?> BuscarPorIdAsync(string id, CancellationToken ct)
    {
        var response = await _dynamoDb.GetItemAsync(new GetItemRequest
        {
            TableName = Tabela,
            Key = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new(id)
            }
        }, ct);

        if (!response.IsItemSet)
            return null;

        return MapearItem(response.Item);
    }

    /// <summary>
    /// Lista todas as imagens (com paginação via Scan).
    /// </summary>
    public async Task<(List<ImagemMetadata> Imagens, string? ProximoToken)> ListarAsync(int limite = 20, string? tokenPaginacao = null, CancellationToken ct = default)
    {
        var request = new ScanRequest
        {
            TableName = Tabela,
            Limit = limite
        };

        if (!string.IsNullOrEmpty(tokenPaginacao))
        {
            request.ExclusiveStartKey = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new(tokenPaginacao)
            };
        }

        var response = await _dynamoDb.ScanAsync(request, ct);

        var imagens = response.Items.Select(MapearItem).ToList();
        var proximo = response.LastEvaluatedKey?.TryGetValue("Id", out var key) == true ? key.S : null;

        return (imagens, proximo);
    }

    /// <summary>
    /// Busca imagens por tag (Scan com filtro).
    /// </summary>
    public async Task<List<ImagemMetadata>> BuscarPorTagAsync(string tag, CancellationToken ct)
    {
        var request = new ScanRequest
        {
            TableName = Tabela,
            FilterExpression = "contains(Tags, :tag)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":tag"] = new(tag)
            }
        };

        var response = await _dynamoDb.ScanAsync(request, ct);
        return response.Items.Select(MapearItem).ToList();
    }

    /// <summary>
    /// Busca imagens por título (Scan com filtro contains).
    /// </summary>
    public async Task<List<ImagemMetadata>> BuscarPorTituloAsync(string termo, CancellationToken ct)
    {
        var request = new ScanRequest
        {
            TableName = Tabela,
            FilterExpression = "contains(Titulo, :termo)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":termo"] = new(termo)
            }
        };

        var response = await _dynamoDb.ScanAsync(request, ct);
        return response.Items.Select(MapearItem).ToList();
    }

    /// <summary>
    /// Deleta metadados de uma imagem.
    /// </summary>
    public async Task DeletarAsync(string id, CancellationToken ct)
    {
        await _dynamoDb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = Tabela,
            Key = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new(id)
            }
        }, ct);

        _logger.LogInformation("DynamoDB DeleteItem: {Id}", id);
    }

    /// <summary>
    /// Conta total de imagens e bytes (para stats).
    /// </summary>
    public async Task<(int Total, long TotalBytes)> ContarAsync(CancellationToken ct)
    {
        var total = 0;
        long totalBytes = 0;
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var request = new ScanRequest
            {
                TableName = Tabela,
                Select = Select.SPECIFIC_ATTRIBUTES,
                ProjectionExpression = "TamanhoBytes",
                ExclusiveStartKey = lastKey
            };

            var response = await _dynamoDb.ScanAsync(request, ct);
            total += response.Count ?? 0;
            totalBytes += response.Items
                .Where(i => i.ContainsKey("TamanhoBytes"))
                .Sum(i => long.Parse(i["TamanhoBytes"].N));

            lastKey = response.LastEvaluatedKey;
        }
        while (lastKey?.Count > 0);

        return (total, totalBytes);
    }

    /// <summary>
    /// Garante que a tabela DynamoDB existe; cria se necessário (dev local).
    /// </summary>
    public async Task GarantirTabelaAsync(CancellationToken ct)
    {
        try
        {
            var tables = await _dynamoDb.ListTablesAsync(ct);
            if (tables.TableNames.Contains(Tabela))
            {
                _logger.LogInformation("Tabela DynamoDB '{Tabela}' já existe.", Tabela);
                return;
            }

            await _dynamoDb.CreateTableAsync(new CreateTableRequest
            {
                TableName = Tabela,
                KeySchema =
                [
                    new KeySchemaElement("Id", KeyType.HASH)
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("Id", ScalarAttributeType.S)
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST
            }, ct);

            _logger.LogInformation("Tabela DynamoDB '{Tabela}' criada com sucesso.", Tabela);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Não foi possível verificar/criar tabela DynamoDB '{Tabela}'.", Tabela);
        }
    }

    private static ImagemMetadata MapearItem(Dictionary<string, AttributeValue> item) => new()
    {
        Id = item.GetValueOrDefault("Id")?.S ?? "",
        Titulo = item.GetValueOrDefault("Titulo")?.S ?? "",
        Descricao = item.GetValueOrDefault("Descricao")?.S ?? "",
        Tags = item.GetValueOrDefault("Tags")?.SS ?? [],
        Formato = item.GetValueOrDefault("Formato")?.S ?? "",
        TamanhoBytes = long.TryParse(item.GetValueOrDefault("TamanhoBytes")?.N, out var tb) ? tb : 0,
        Largura = int.TryParse(item.GetValueOrDefault("Largura")?.N, out var w) ? w : 0,
        Altura = int.TryParse(item.GetValueOrDefault("Altura")?.N, out var h) ? h : 0,
        S3Key = item.GetValueOrDefault("S3Key")?.S ?? "",
        S3Bucket = item.GetValueOrDefault("S3Bucket")?.S ?? "",
        UsuarioId = item.GetValueOrDefault("UsuarioId")?.S ?? "",
        DataUpload = DateTime.TryParse(item.GetValueOrDefault("DataUpload")?.S, out var dt) ? dt : DateTime.MinValue,
        Publica = item.GetValueOrDefault("Publica")?.BOOL ?? true
    };
}
