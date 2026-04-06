using SmartGallery.Maui.Services;
using SmartGallery.Shared.DTOs;
using System.Collections.ObjectModel;

namespace SmartGallery.Maui;

public partial class MainPage : ContentPage
{
	private readonly GaleriaApiClient _api;
	private readonly ObservableCollection<ImagemItemViewModel> _imagens = [];

	public MainPage(GaleriaApiClient api)
	{
		InitializeComponent();
		_api = api;
		GaleriaView.ItemsSource = _imagens;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await VerificarConexaoAsync();
		await CarregarImagensAsync();
	}

	private async Task VerificarConexaoAsync()
	{
		var online = await _api.HealthCheckAsync();
		LblStatus.Text = online
			? "✅ Conectado à API (LocalStack)"
			: "❌ API offline — verifique se o servidor está rodando";
		LblStatus.TextColor = online ? Color.FromArgb("#3fb950") : Color.FromArgb("#f85149");
	}

	private async Task CarregarImagensAsync()
	{
		try
		{
			var resultado = await _api.ListarAsync();
			if (resultado is null) return;

			_imagens.Clear();
			foreach (var img in resultado.Imagens)
			{
				_imagens.Add(new ImagemItemViewModel
				{
					Id = img.Id,
					Titulo = img.Titulo,
					Formato = img.Formato.ToUpperInvariant(),
					TamanhoBytes = img.TamanhoBytes,
					UrlThumbnail = img.UrlThumbnail,
					Tags = img.Tags
				});
			}

			LblTotal.Text = $"{resultado.Total} imagens";
			LblTamanho.Text = FormatarBytes(_imagens.Sum(i => i.TamanhoBytes));
		}
		catch (Exception ex)
		{
			LblStatus.Text = $"Erro: {ex.Message}";
			LblStatus.TextColor = Color.FromArgb("#f85149");
		}
	}

	private async void OnUploadClicked(object? sender, EventArgs e)
	{
		try
		{
			var resultado = await FilePicker.Default.PickAsync(new PickOptions
			{
				FileTypes = FilePickerFileType.Images,
				PickerTitle = "Selecione uma imagem"
			});

			if (resultado is null) return;

			var titulo = await DisplayPromptAsync("Upload", "Título da imagem:", initialValue: Path.GetFileNameWithoutExtension(resultado.FileName));
			if (string.IsNullOrEmpty(titulo)) return;

			var tags = await DisplayPromptAsync("Tags", "Tags separadas por vírgula:", placeholder: "paisagem, natureza, rio");

			using var stream = await resultado.OpenReadAsync();
			var response = await _api.UploadAsync(stream, resultado.FileName, titulo, tags: tags);

			if (response is not null)
			{
				await DisplayAlertAsync("Sucesso", $"'{response.Titulo}' enviada com sucesso!", "OK");
				await CarregarImagensAsync();
			}
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Erro", $"Falha no upload: {ex.Message}", "OK");
		}
	}

	private async void OnRefreshClicked(object? sender, EventArgs e)
	{
		await VerificarConexaoAsync();
		await CarregarImagensAsync();
	}

	private async void OnBuscaCompleted(object? sender, EventArgs e)
	{
		await BuscarAsync();
	}

	private async void OnBuscaClicked(object? sender, EventArgs e)
	{
		await BuscarAsync();
	}

	private async Task BuscarAsync()
	{
		var termo = EntryBusca.Text?.Trim();
		if (string.IsNullOrEmpty(termo))
		{
			await CarregarImagensAsync();
			return;
		}

		try
		{
			var resultado = await _api.BuscarAsync(tag: termo);
			if (resultado is null) return;

			_imagens.Clear();
			foreach (var img in resultado.Imagens)
			{
				_imagens.Add(new ImagemItemViewModel
				{
					Id = img.Id,
					Titulo = img.Titulo,
					Formato = img.Formato.ToUpperInvariant(),
					TamanhoBytes = img.TamanhoBytes,
					UrlThumbnail = img.UrlThumbnail,
					Tags = img.Tags
				});
			}

			LblTotal.Text = $"{resultado.Total} encontradas";
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Erro", $"Falha na busca: {ex.Message}", "OK");
		}
	}

	private async void OnImagemSelecionada(object? sender, SelectionChangedEventArgs e)
	{
		if (e.CurrentSelection.FirstOrDefault() is not ImagemItemViewModel item) return;
		GaleriaView.SelectedItem = null;

		var acao = await DisplayActionSheetAsync(item.Titulo, "Cancelar", "Deletar", "Ver Detalhes");

		switch (acao)
		{
			case "Ver Detalhes":
				var detalhe = await _api.DetalheAsync(item.Id);
				if (detalhe is not null)
				{
					await DisplayAlertAsync(detalhe.Titulo,
						$"Formato: {detalhe.Formato}\n" +
						$"Tamanho: {FormatarBytes(detalhe.TamanhoBytes)}\n" +
						$"Tags: {string.Join(", ", detalhe.Tags)}\n" +
						$"Upload: {detalhe.DataUpload:dd/MM/yyyy HH:mm}",
						"OK");
				}
				break;

			case "Deletar":
				var confirma = await DisplayAlertAsync("Confirmar", $"Deletar '{item.Titulo}'?", "Sim", "Não");
				if (confirma)
				{
					await _api.DeletarAsync(item.Id);
					await CarregarImagensAsync();
				}
				break;
		}
	}

	private static string FormatarBytes(long bytes) => bytes switch
	{
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
		_ => $"{bytes / (1024.0 * 1024.0):F1} MB"
	};
}

/// <summary>ViewModel para cada item na galeria.</summary>
public class ImagemItemViewModel
{
	public string Id { get; set; } = "";
	public string Titulo { get; set; } = "";
	public string Formato { get; set; } = "";
	public long TamanhoBytes { get; set; }
	public string UrlThumbnail { get; set; } = "";
	public List<string> Tags { get; set; } = [];
	public string TamanhoFormatado => TamanhoBytes switch
	{
		< 1024 => $"{TamanhoBytes} B",
		< 1024 * 1024 => $"{TamanhoBytes / 1024.0:F1} KB",
		_ => $"{TamanhoBytes / (1024.0 * 1024.0):F1} MB"
	};
}
