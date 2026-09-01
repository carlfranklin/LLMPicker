using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LLMPicker
{
	public partial class MainWindow : Window
	{
		private const string EnvBaseUrl = "COPILOT_PROVIDER_BASE_URL";
		private const string EnvModel = "COPILOT_MODEL";

		private const string DefaultHostAddress = "192.168.1.155";
		private const int OllamaPort = 11434;
		private const int FoundryLocalPort = 51331;
		private const int LlamaCppPort = 8080;

		private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
		private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
		private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");

		private readonly string _hostAddress = LoadHostAddress();
		private bool _loading = true;

		private readonly ProviderManager _providerManager;
		private readonly SettingsManager _settingsManager;

		private List<string> _ollamaModels = [];
		private List<string> _foundryModels = [];
		private List<string> _llamaCppModels = [];

		private string? SelectedUrl => _providerManager.GetSelectedUrl(ProviderCombo.SelectedIndex);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
		private static extern IntPtr SendMessageTimeout(
			IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam,
			uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

		private static readonly IntPtr HwndBroadcast = new(0xffff);
		private const uint WmSettingChange = 0x001A;
		private const uint SmtoAbortIfHung = 0x0002;

		public MainWindow()
		{
			InitializeComponent();

			_providerManager = new ProviderManager(_hostAddress, this);
			_settingsManager = new SettingsManager(SettingsPath);

			InitializeComponents();
			LoadModels();
			PopulateProviders();
			RefreshCurrent();
			_loading = false;
			UpdatePreview();
		}

		private void InitializeComponents()
		{
			ProviderCombo.SelectionChanged += ProviderCombo_SelectionChanged;
			ModelCombo.SelectionChanged += ModelCombo_SelectionChanged;
			ApplyBtn.Click += ApplyBtn_Click;
		}

		private string BuildOpenAiUrl(int port) =>
			_providerManager.BuildOpenAiUrl(port);

		private static string LoadHostAddress()
		{
			var hostAddress = DefaultHostAddress;

			try
			{
				if (File.Exists(ConfigPath))
				{
					using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
					if (doc.RootElement.TryGetProperty("hostAddress", out var hostEl))
					{
						var configuredHost = hostEl.GetString();
						if (!string.IsNullOrWhiteSpace(configuredHost))
							hostAddress = configuredHost;
					}
				}
			}
			catch { /* non-critical */ }

			return NormalizeHostAddress(hostAddress);
		}

		private static string NormalizeHostAddress(string hostAddress)
		{
			var normalized = hostAddress.Trim().TrimEnd('/');

			const string httpPrefix = "http://";
			const string httpsPrefix = "https://";

			if (normalized.StartsWith(httpPrefix, StringComparison.OrdinalIgnoreCase))
				normalized = normalized[httpPrefix.Length..];
			else if (normalized.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
				normalized = normalized[httpsPrefix.Length..];

			return normalized;
		}

		private void LoadModels()
		{
			var configPath = Path.Combine(AppContext.BaseDirectory, "models.json");
			if (!File.Exists(configPath))
			{
				_llamaCppModels.Add("qwen3-coder-next");
				return;
			}

			try
			{
				var json = File.ReadAllText(configPath);
				using var doc = JsonDocument.Parse(json);
				_ollamaModels.AddRange(ReadModels(doc.RootElement, "models"));
				_llamaCppModels.AddRange(ReadModels(doc.RootElement, "llamaCppModels"));
			}
			catch { /* non-critical */ }

			if (_llamaCppModels.Count == 0)
				_llamaCppModels.Add("qwen3-coder-next");
		}

		private static IEnumerable<string> ReadModels(JsonElement root, string propertyName)
		{
			if (!root.TryGetProperty(propertyName, out var modelsEl) ||
				modelsEl.ValueKind != JsonValueKind.Array)
			{
				return [];
			}

			var models = new List<string>();
			foreach (var el in modelsEl.EnumerateArray())
			{
				var name = el.GetString() ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(name))
					models.Add(name);
			}

			return models;
		}

		private void PopulateModelCombo(IEnumerable<string> models, string? lastModel)
		{
			ModelCombo.Items.Clear();
			foreach (var m in models)
				ModelCombo.Items.Add(m);

			if (ModelCombo.Items.Count == 0) return;

			var idx = lastModel is not null ? ModelCombo.Items.IndexOf(lastModel) : -1;
			ModelCombo.SelectedIndex = idx >= 0 ? idx : 0;
		}

		private async Task<List<string>> FetchFoundryModelsAsync()
		{
			try
			{
				var json = await _http.GetStringAsync(_providerManager.GetFoundryModelsUrl());
				var doc = JsonDocument.Parse(json);
				var list = new List<string>();
				foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
				{
					if (item.TryGetProperty("id", out var idEl))
					{
						var id = idEl.GetString();
						if (!string.IsNullOrEmpty(id))
							list.Add(id);
					}
				}
				return list;
			}
			catch
			{
				return [];
			}
		}

		private void PopulateProviders()
		{
			_providerManager.PopulateProviders(ProviderCombo);

			var currentUrl = GetUserEnv(EnvBaseUrl);
			ProviderCombo.SelectedIndex = _providerManager.GetProviderIndexByUrl(currentUrl);
		}

		private void RefreshCurrent()
		{
			CurrentBaseUrl.Text = GetUserEnv(EnvBaseUrl) ?? "(not set)";
			CurrentModel.Text = GetUserEnv(EnvModel) ?? "(not set)";
		}

		private void UpdatePreview()
		{
			if (_loading) return;

			PreviewBaseUrl.Text = SelectedUrl ?? "(not set)";
			PreviewModel.Text = ProviderCombo.SelectedIndex > 0
				? (ModelCombo.SelectedItem as string ?? "(not set)")
				: "(not set)";
		}

		private async void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			int idx = ProviderCombo.SelectedIndex;

			if (idx == ProviderManager.OllamaProviderIndex)
			{
				await HandleOllamaProviderSelection();
			}
			else if (idx == ProviderManager.FoundryLocalProviderIndex)
			{
				await HandleFoundryLocalProviderSelection();
			}
			else if (idx == ProviderManager.LlamaCppProviderIndex)
			{
				await HandleLlamaCppProviderSelection();
			}
			else
			{
				HandleDefaultProviderSelection();
			}

			UpdatePreview();
		}

		private async Task HandleOllamaProviderSelection()
		{
			ModelRow.Visibility = Visibility.Visible;
			ModelCombo.IsEnabled = true;
			ApplyBtn.IsEnabled = true;
			RefreshModelsBtn.Visibility = Visibility.Visible;
			RefreshModelsBtn.Tag = ProviderType.Ollama;

			var discoveredModels = await _providerManager.GetOllamaModelsAsync();
			if (discoveredModels.Count > 0)
			{
				_ollamaModels = discoveredModels;
			}

			PopulateModelCombo(_ollamaModels, _settingsManager.GetLastModel(ProviderType.Ollama));
		}

		private async Task HandleFoundryLocalProviderSelection()
		{
			ModelRow.Visibility = Visibility.Visible;
			ModelCombo.IsEnabled = false;
			ApplyBtn.IsEnabled = false;
			RefreshModelsBtn.Visibility = Visibility.Collapsed; // FoundryLocal already fetches models dynamically
			ModelCombo.Items.Clear();
			ModelCombo.Items.Add("Loading…");
			ModelCombo.SelectedIndex = 0;

			_foundryModels = await FetchFoundryModelsAsync();

			ModelCombo.IsEnabled = true;
			ApplyBtn.IsEnabled = true;

			if (_foundryModels.Count > 0)
				PopulateModelCombo(_foundryModels, _settingsManager.GetLastModel(ProviderType.FoundryLocal));
			else
			{
				ModelCombo.Items.Clear();
				ModelCombo.Items.Add("(no models found)");
				ModelCombo.SelectedIndex = 0;
			}
		}

		private async Task HandleLlamaCppProviderSelection()
		{
			ModelRow.Visibility = Visibility.Visible;
			ModelCombo.IsEnabled = false;
			ApplyBtn.IsEnabled = false;
			RefreshModelsBtn.Visibility = Visibility.Visible;
			RefreshModelsBtn.Tag = ProviderType.LlamaCpp;
			ModelCombo.Items.Clear();
			ModelCombo.Items.Add("Loading…");
			ModelCombo.SelectedIndex = 0;

			var discoveredModels = await _providerManager.GetLlamaCppModelsAsync();

			ModelCombo.IsEnabled = true;
			ApplyBtn.IsEnabled = true;

			if (discoveredModels.Count > 0)
			{
				_llamaCppModels = discoveredModels;
			}

			PopulateModelCombo(_llamaCppModels, _settingsManager.GetLastModel(ProviderType.LlamaCpp));
		}

		private void HandleDefaultProviderSelection()
		{
			ModelRow.Visibility = Visibility.Collapsed;
			RefreshModelsBtn.Visibility = Visibility.Collapsed;
		}

		private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
			UpdatePreview();

		private async void RefreshModelsBtn_Click(object sender, RoutedEventArgs e)
		{
			if (RefreshModelsBtn.Tag is not ProviderType providerType) return;

			IsEnabled = false;
			Cursor = System.Windows.Input.Cursors.Wait;
			try
			{
				List<string> discoveredModels = [];

				if (providerType == ProviderType.Ollama)
				{
					discoveredModels = await _providerManager.GetOllamaModelsAsync();
					if (discoveredModels.Count > 0)
					{
						_ollamaModels = discoveredModels;
					}
				}
				else if (providerType == ProviderType.LlamaCpp)
				{
					discoveredModels = await _providerManager.GetLlamaCppModelsAsync();
					if (discoveredModels.Count > 0)
					{
						_llamaCppModels = discoveredModels;
					}
				}
				if (discoveredModels.Count > 0)
				{
					PopulateModelCombo(discoveredModels, ModelCombo.SelectedItem as string);

					// Clear any existing settings for this provider to force fresh selection
					if (providerType == ProviderType.Ollama)
						_settingsManager.SaveLastModel(ProviderType.Ollama, "");
					else if (providerType == ProviderType.LlamaCpp)
						_settingsManager.SaveLastModel(ProviderType.LlamaCpp, "");
				}
				else
				{
					// Show error message
					System.Windows.MessageBox.Show($"Failed to fetch models from {providerType}. Please check if the provider is running and accessible.", "Model Discovery Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
			}
			finally
			{
				Cursor = null;
				IsEnabled = true;
			}
		}

		private void ApplyBtn_Click(object sender, RoutedEventArgs e)
		{
			IsEnabled = false;
			Cursor = System.Windows.Input.Cursors.Wait;
			try
			{
				var providerIdx = ProviderCombo.SelectedIndex;
				var isProvider = providerIdx > 0;

				if (isProvider)
				{
					ApplyProviderSelection(providerIdx);
				}
				else
				{
					ClearEnvironmentVariables();
				}

				BroadcastChange();
				RefreshCurrent();
				UpdatePreview();
			}
			finally
			{
				Cursor = null;
				IsEnabled = true;
			}
		}

		private void ApplyProviderSelection(int providerIdx)
		{
			var model = ModelCombo.SelectedItem as string ?? string.Empty;
			SetUserEnv(EnvBaseUrl, SelectedUrl);
			SetUserEnv(EnvModel, model);

			var providerType = providerIdx switch
			{
				_ when providerIdx == ProviderManager.OllamaProviderIndex => ProviderType.Ollama,
				_ when providerIdx == ProviderManager.FoundryLocalProviderIndex => ProviderType.FoundryLocal,
				_ when providerIdx == ProviderManager.LlamaCppProviderIndex => ProviderType.LlamaCpp,
				_ => ProviderType.Default
			};

			_settingsManager.SaveLastModel(providerType, model);
		}

		private void ClearEnvironmentVariables()
		{
			SetUserEnv(EnvBaseUrl, null);
			SetUserEnv(EnvModel, null);
		}


		private static string? GetUserEnv(string name) =>
			Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

		private static void SetUserEnv(string name, string? value) =>
			Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);

		private static void BroadcastChange() =>
			SendMessageTimeout(HwndBroadcast, WmSettingChange, UIntPtr.Zero,
				"Environment", SmtoAbortIfHung, 5000, out _);
	}
}
