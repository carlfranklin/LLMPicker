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

        private const string OllamaUrl        = "http://192.168.1.61:11434/v1";
        private const string FoundryLocalUrl  = "http://192.168.1.61:51331/v1";
        private const string FoundryModelsUrl = "http://192.168.1.61:51331/v1/models";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private List<string> _ollamaModels  = [];
        private List<string> _foundryModels = [];

        private string? SelectedUrl => ProviderCombo.SelectedIndex switch
        {
            1 => OllamaUrl,
            2 => FoundryLocalUrl,
            _ => null,
        };

        private static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "settings.json");

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
            LoadOllamaModels();
            PopulateProviders();
            RefreshCurrent();
            _loading = false;
            UpdatePreview();
        }

        private void LoadOllamaModels()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "models.json");
            if (!File.Exists(configPath)) return;

            try
            {
                var json = File.ReadAllText(configPath);
                var doc  = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.GetProperty("models").EnumerateArray())
                {
                    var name = el.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(name))
                        _ollamaModels.Add(name);
                }
            }
            catch { /* non-critical */ }
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

        private static async Task<List<string>> FetchFoundryModelsAsync()
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

            var currentUrl = GetUserEnv(EnvBaseUrl);
            ProviderCombo.SelectedIndex = currentUrl switch
            {
                OllamaUrl       => 1,
                FoundryLocalUrl => 2,
                _               => 0,
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

            if (idx == 1) // Ollama
            {
                ModelRow.Visibility = Visibility.Visible;
                PopulateModelCombo(_ollamaModels, LoadSetting("lastOllamaModel") ?? LoadSetting("lastModel"));
            }
            else if (idx == 2) // FoundryLocal
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

                    if (providerIdx == 1)      SaveSetting("lastOllamaModel",  model);
                    else if (providerIdx == 2) SaveSetting("lastFoundryModel", model);
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