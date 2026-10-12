namespace MobileApp;

public partial class MainPage : ContentPage
{
	private int _count;

	public MainPage()
	{
		InitializeComponent();
	}

	private void OnGreetClicked(object? sender, EventArgs e)
	{
		NameEntry.Unfocus();
		var name = NameEntry.Text?.Trim();
		if (string.IsNullOrEmpty(name))
		{
			ErrorLabel.IsVisible = true;
			GreetingLabel.IsVisible = false;
			SemanticScreenReader.Announce(ErrorLabel.Text);
			return;
		}

		ErrorLabel.IsVisible = false;
		GreetingLabel.Text = $"Hello, {name}!";
		GreetingLabel.IsVisible = true;
		NameEntry.Text = "";
	}

	private void OnIncrementClicked(object? sender, EventArgs e)
	{
		_count++;
		CounterLabel.Text = $"Count: {_count}";
	}
}
