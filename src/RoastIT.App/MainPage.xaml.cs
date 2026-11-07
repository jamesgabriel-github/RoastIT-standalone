using RoastIT.App.Services;

namespace RoastIT.App;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	/// <summary>
	/// Android Back closes the top open layer (PIN prompt, dialog, bottom sheet, full sheet) and
	/// never exits the app from inside one. With no layer open, normal Back behaviour applies.
	/// </summary>
	protected override bool OnBackButtonPressed()
	{
		var back = Handler?.MauiContext?.Services.GetService<BackNavigationService>();
		if (back is not null && back.HasLayers)
		{
			_ = Dispatcher.DispatchAsync(back.TryCloseTopAsync);
			return true;
		}

		return base.OnBackButtonPressed();
	}
}
