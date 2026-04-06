using System.Net.Http.Json;
using SmartGallery.Shared.DTOs;

namespace SmartGallery.Maui.Services;

/// <summary>
/// Cliente HTTP para a Smart Gallery API.
/// </summary>
public class GaleriaApiClient
{
    private readonly HttpClient _http;

    public GaleriaApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>URL base da API (configurável).</summary>
    public static string BaseUrl { get; set; } = "http://localhost:5050";

    /// <summary>
    /// Lista imagens com paginação.
    /// </summary>
    public async Task<ListagemImagensResponse?> ListarAsync(int limite = 20, string? token = null)
    {
        var url = $"{BaseUrl}/api/imagens?limite={limite}";
        if (!string.IsNullOrEmpty(token))
            url += $"&token={token}";

        return await _http.GetFromJsonAsync<ListagemImagensResponse>(url);
    }

    /// <summary>
    /// Busca detalhes de uma imagem por ID.
    /// </summary>
    public async Task<ImagemDetalheResponse?> DetalheAsync(string id)
    {
        return await _http.GetFromJsonAsync<ImagemDetalheResponse>($"{BaseUrl}/api/imagens/{id}");
    }

    /// <summary>
    /// Faz upload de uma imagem para a API.
    /// </summary>
    public async Task<UploadImagemResponse?> UploadAsync(Stream arquivo, string nomeArquivo, string titulo, string? descricao = null, string? tags = null)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(arquivo), "arquivo", nomeArquivo);
        content.Add(new StringContent(titulo), "titulo");

        if (!string.IsNullOrEmpty(descricao))
            content.Add(new StringContent(descricao), "descricao");
        if (!string.IsNullOrEmpty(tags))
            content.Add(new StringContent(tags), "tags");

        var response = await _http.PostAsync($"{BaseUrl}/api/imagens", content);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UploadImagemResponse>();
    }

    /// <summary>
    /// Deleta uma imagem por ID.
    /// </summary>
    public async Task<bool> DeletarAsync(string id)
    {
        var response = await _http.DeleteAsync($"{BaseUrl}/api/imagens/{id}");
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Busca imagens por tag ou termo.
    /// </summary>
    public async Task<ListagemImagensResponse?> BuscarAsync(string? tag = null, string? termo = null)
    {
        var url = $"{BaseUrl}/api/imagens/busca?";
        if (!string.IsNullOrEmpty(tag)) url += $"tag={Uri.EscapeDataString(tag)}";
        if (!string.IsNullOrEmpty(termo)) url += $"termo={Uri.EscapeDataString(termo)}";

        return await _http.GetFromJsonAsync<ListagemImagensResponse>(url);
    }

    /// <summary>
    /// Retorna estatísticas da galeria.
    /// </summary>
    public async Task<GaleriaStatsResponse?> StatsAsync()
    {
        return await _http.GetFromJsonAsync<GaleriaStatsResponse>($"{BaseUrl}/api/imagens/stats");
    }

    /// <summary>
    /// Verifica saúde da API.
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{BaseUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
