using Microsoft.Extensions.Logging;
using SmartGallery.Maui.Services;

namespace SmartGallery.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// HttpClient + API Client
		builder.Services.AddSingleton<HttpClient>();
		builder.Services.AddSingleton<GaleriaApiClient>();

		// Páginas
		builder.Services.AddTransient<MainPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
