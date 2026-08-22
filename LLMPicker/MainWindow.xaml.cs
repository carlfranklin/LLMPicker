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
        private const string EnvBaseUrl   = "COPILOT_PROVIDER_BASE_URL";
        private const string EnvModel     = "COPILOT_MODEL";

        private const string DefaultHostAddress = "192.168.1.23";
        private const int OllamaPort            = 11434;
        private const int FoundryLocalPort      = 51331;
        private const int LlamaCppPort          = 8080;

        private const int DefaultProviderIndex      = 0;
        private const int OllamaProviderIndex       = 1;
        private const int FoundryLocalProviderIndex = 2;
        private const int LlamaCppProviderIndex     = 3;

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private static readonly string ConfigPath =
            Path.Combine(AppContext.BaseDirectory, "config.json");

        private static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "settings.json");

        private readonly string _hostAddress = LoadHostAddress();

        private List<string> _ollamaModels    = [];
        private List<string> _foundryModels   = [];
        private List<string> _llamaCppModels  = [];

        private string OllamaUrl       => BuildOpenAiUrl(OllamaPort);
        private string FoundryLocalUrl => BuildOpenAiUrl(FoundryLocalPort);
        private string FoundryModelsUrl => $"{FoundryLocalUrl}/models";
        private string LlamaCppUrl     => BuildOpenAiUrl(LlamaCppPort);

        private string? SelectedUrl => ProviderCombo.SelectedIndex switch
        {
            OllamaProviderIndex       => OllamaUrl,
            FoundryLocalProviderIndex => FoundryLocalUrl,
            LlamaCppProviderIndex     => LlamaCppUrl,
            _                         => null,
        };

        private bool _loading = true;

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
            LoadModels();
            PopulateProviders();
            RefreshCurrent();
            _loading = false;
            UpdatePreview();
        }

        private string BuildOpenAiUrl(int port) =>
            $"http://{_hostAddress}:{port}/v1";

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
                _llamaCppModels.Add("qwen3-coder");
                return;
            }

            try
            {
                var json = File.ReadAllText(configPath);
                using var doc  = JsonDocument.Parse(json);
                _ollamaModels.AddRange(ReadModels(doc.RootElement, "models"));
                _llamaCppModels.AddRange(ReadModels(doc.RootElement, "llamaCppModels"));
            }
            catch { /* non-critical */ }

            if (_llamaCppModels.Count == 0)
                _llamaCppModels.Add("qwen3-coder");
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
                var json = await _http.GetStringAsync(FoundryModelsUrl);
                var doc  = JsonDocument.Parse(json);
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
            ProviderCombo.Items.Add("Default");
            ProviderCombo.Items.Add("Ollama");
            ProviderCombo.Items.Add("FoundryLocal");
            ProviderCombo.Items.Add("Llama.cpp");

            var currentUrl = GetUserEnv(EnvBaseUrl);
            ProviderCombo.SelectedIndex = currentUrl switch
            {
                var url when string.Equals(url, OllamaUrl, StringComparison.OrdinalIgnoreCase)       => OllamaProviderIndex,
                var url when string.Equals(url, FoundryLocalUrl, StringComparison.OrdinalIgnoreCase) => FoundryLocalProviderIndex,
                var url when string.Equals(url, LlamaCppUrl, StringComparison.OrdinalIgnoreCase)     => LlamaCppProviderIndex,
                _                                                                                   => DefaultProviderIndex,
            };
        }

        private void RefreshCurrent()
        {
            CurrentBaseUrl.Text = GetUserEnv(EnvBaseUrl) ?? "(not set)";
            CurrentModel.Text   = GetUserEnv(EnvModel)   ?? "(not set)";
        }

        private void UpdatePreview()
        {
            if (_loading) return;

            PreviewBaseUrl.Text = SelectedUrl ?? "(not set)";
            PreviewModel.Text   = ProviderCombo.SelectedIndex > 0
                ? (ModelCombo.SelectedItem as string ?? "(not set)")
                : "(not set)";
        }

        private async void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = ProviderCombo.SelectedIndex;

            if (idx == OllamaProviderIndex)
            {
                ModelRow.Visibility = Visibility.Visible;
                ModelCombo.IsEnabled = true;
                ApplyBtn.IsEnabled = true;
                PopulateModelCombo(_ollamaModels, LoadSetting("lastOllamaModel") ?? LoadSetting("lastModel"));
            }
            else if (idx == FoundryLocalProviderIndex)
            {
                ModelRow.Visibility  = Visibility.Visible;
                ModelCombo.IsEnabled = false;
                ApplyBtn.IsEnabled   = false;
                ModelCombo.Items.Clear();
                ModelCombo.Items.Add("Loading…");
                ModelCombo.SelectedIndex = 0;

                _foundryModels = await FetchFoundryModelsAsync();

                ModelCombo.IsEnabled = true;
                ApplyBtn.IsEnabled   = true;

                if (_foundryModels.Count > 0)
                    PopulateModelCombo(_foundryModels, LoadSetting("lastFoundryModel"));
                else
                {
                    ModelCombo.Items.Clear();
                    ModelCombo.Items.Add("(no models found)");
                    ModelCombo.SelectedIndex = 0;
                }
            }
            else if (idx == LlamaCppProviderIndex)
            {
                ModelRow.Visibility = Visibility.Visible;
                ModelCombo.IsEnabled = true;
                ApplyBtn.IsEnabled = true;
                PopulateModelCombo(_llamaCppModels, LoadSetting("lastLlamaCppModel"));
            }
            else
            {
                ModelRow.Visibility = Visibility.Collapsed;
            }

            UpdatePreview();
        }

        private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            UpdatePreview();

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                int  providerIdx = ProviderCombo.SelectedIndex;
                bool isProvider  = providerIdx > 0;

                if (isProvider)
                {
                    var model = ModelCombo.SelectedItem as string ?? string.Empty;
                    SetUserEnv(EnvBaseUrl, SelectedUrl);
                    SetUserEnv(EnvModel,   model);

                    if (providerIdx == OllamaProviderIndex)            SaveSetting("lastOllamaModel",  model);
                    else if (providerIdx == FoundryLocalProviderIndex) SaveSetting("lastFoundryModel", model);
                    else if (providerIdx == LlamaCppProviderIndex)     SaveSetting("lastLlamaCppModel", model);
                }
                else
                {
                    SetUserEnv(EnvBaseUrl, null);
                    SetUserEnv(EnvModel,   null);
                }

                BroadcastChange();
                RefreshCurrent();
                UpdatePreview();
            }
            finally
            {
                Cursor    = null;
                IsEnabled = true;
            }
        }

        private static string? LoadSetting(string key)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                return doc.RootElement.TryGetProperty(key, out var el) ? el.GetString() : null;
            }
            catch { return null; }
        }

        private static void SaveSetting(string key, string value)
        {
            try
            {
                var settings = new Dictionary<string, string>();
                if (File.Exists(SettingsPath))
                {
                    try
                    {
                        var existing = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                        foreach (var prop in existing.RootElement.EnumerateObject())
                            settings[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                    catch { }
                }
                settings[key] = value;
                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* non-critical */ }
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
