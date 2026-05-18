using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LLMPicker
{
    public partial class MainWindow : Window
    {
        private const string EnvBaseUrl    = "COPILOT_PROVIDER_BASE_URL";
        private const string EnvModel      = "COPILOT_MODEL";
        private const string OllamaUrl     = "http://192.168.1.61:11434/v1";
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
            LoadModels();
            PopulateProviders();
            RefreshCurrent();
            _loading = false;
            UpdatePreview();
        }

        private void LoadModels()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "models.json");
            if (!File.Exists(configPath))
            {
                ModelCombo.Items.Add("(models.json not found)");
                return;
            }

            try
            {
                var json = File.ReadAllText(configPath);
                var doc  = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.GetProperty("models").EnumerateArray())
                    ModelCombo.Items.Add(el.GetString() ?? string.Empty);

                // Restore last selected model, fall back to first item
                var lastModel = LoadLastModel();
                var idx = lastModel is not null
                    ? ModelCombo.Items.IndexOf(lastModel)
                    : -1;
                ModelCombo.SelectedIndex = idx >= 0 ? idx : (ModelCombo.Items.Count > 0 ? 0 : -1);
            }
            catch
            {
                ModelCombo.Items.Add("(error reading models.json)");
            }
        }

        private void PopulateProviders()
        {
            ProviderCombo.Items.Add("Default");
            ProviderCombo.Items.Add("Ollama");

            // Pre-select based on current env state
            var currentUrl = GetUserEnv(EnvBaseUrl);
            ProviderCombo.SelectedIndex = currentUrl == OllamaUrl ? 1 : 0;
        }

        private void RefreshCurrent()
        {
            CurrentBaseUrl.Text = GetUserEnv(EnvBaseUrl) ?? "(not set)";
            CurrentModel.Text   = GetUserEnv(EnvModel)   ?? "(not set)";
        }

        private void UpdatePreview()
        {
            if (_loading) return;

            bool isOllama = ProviderCombo.SelectedIndex == 1;

            PreviewBaseUrl.Text = isOllama ? OllamaUrl : "(not set)";
            PreviewModel.Text   = isOllama
                ? (ModelCombo.SelectedItem as string ?? "(not set)")
                : "(not set)";
        }

        private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isOllama = ProviderCombo.SelectedIndex == 1;
            ModelRow.Visibility = isOllama ? Visibility.Visible : Visibility.Collapsed;
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
                bool isOllama = ProviderCombo.SelectedIndex == 1;

                if (isOllama)
                {
                    var model = ModelCombo.SelectedItem as string ?? string.Empty;
                    SetUserEnv(EnvBaseUrl, OllamaUrl);
                    SetUserEnv(EnvModel,   model);
                    SaveLastModel(model);
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
                Cursor = null;
                IsEnabled = true;
            }
        }

        private static string? LoadLastModel()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                return doc.RootElement.TryGetProperty("lastModel", out var el) ? el.GetString() : null;
            }
            catch { return null; }
        }

        private static void SaveLastModel(string model)
        {
            try
            {
                var json = JsonSerializer.Serialize(new { lastModel = model },
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
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