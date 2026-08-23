namespace LLMPicker
{
    using System;
        using System.Collections.Generic;
        using System.Net.Http;
        using System.Text.Json;
        using System.Threading.Tasks;
        using System.Windows;
        using System.Windows.Controls;

    public enum ProviderType
    {
        Default,
        Ollama,
        FoundryLocal,
        LlamaCpp
    }

    public class ProviderManager
    {
        private readonly string _hostAddress;
        private readonly MainWindow _window;
                private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

                public const int DefaultProviderIndex = 0;
                public const int OllamaProviderIndex = 1;
                public const int FoundryLocalProviderIndex = 2;
                public const int LlamaCppProviderIndex = 3;

                private const int OllamaPort = 11434;
                private const int FoundryLocalPort = 51331;
                private const int LlamaCppPort = 8080;

                public string OllamaUrl => BuildOpenAiUrl(OllamaPort);
                public string FoundryLocalUrl => BuildOpenAiUrl(FoundryLocalPort);
                public string FoundryModelsUrl => $"{FoundryLocalUrl}/models";
                public string LlamaCppUrl => $"http://{_hostAddress}:{LlamaCppPort}/v1";
                public string OllamaTagsUrl => $"http://{_hostAddress}:{OllamaPort}/api/tags";
                public string LlamaCppModelsUrl => $"{LlamaCppUrl}/models";

                public ProviderManager(string hostAddress, MainWindow window)
                {
                    _hostAddress = hostAddress;
                    _window = window;
                }

        public string BuildOpenAiUrl(int port) =>
            $"http://{_hostAddress}:{port}/v1";

        public string? GetSelectedUrl(int selectedIndex) =>
            selectedIndex switch
            {
                OllamaProviderIndex => OllamaUrl,
                FoundryLocalProviderIndex => FoundryLocalUrl,
                LlamaCppProviderIndex => LlamaCppUrl,
                _ => null
            };

        public int GetProviderIndexByUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return DefaultProviderIndex;

            return url switch
            {
                var u when string.Equals(u, OllamaUrl, StringComparison.OrdinalIgnoreCase) => OllamaProviderIndex,
                var u when string.Equals(u, FoundryLocalUrl, StringComparison.OrdinalIgnoreCase) => FoundryLocalProviderIndex,
                var u when string.Equals(u, LlamaCppUrl, StringComparison.OrdinalIgnoreCase) => LlamaCppProviderIndex,
                _ => DefaultProviderIndex
            };
        }

        public void PopulateProviders(ComboBox providerCombo)
        {
            providerCombo.Items.Add("Default");
            providerCombo.Items.Add("Ollama");
            providerCombo.Items.Add("FoundryLocal");
            providerCombo.Items.Add("Llama.cpp");
        }

        public string GetProviderName(int selectedIndex)
        {
            return selectedIndex switch
            {
                OllamaProviderIndex => "Ollama",
                FoundryLocalProviderIndex => "FoundryLocal",
                LlamaCppProviderIndex => "Llama.cpp",
                _ => "Default"
            };
        }

                public string GetFoundryModelsUrl() => FoundryModelsUrl;

                        public async Task<List<string>> GetOllamaModelsAsync()
                        {
                            try
                            {
                                var json = await _http.GetStringAsync(OllamaTagsUrl);
                                using var doc = JsonDocument.Parse(json);
                                var models = new List<string>();
                
                                if (doc.RootElement.TryGetProperty("models", out var modelsEl))
                                {
                                    foreach (var model in modelsEl.EnumerateArray())
                                    {
                                        if (model.TryGetProperty("name", out var nameEl))
                                        {
                                            var name = nameEl.GetString();
                                            if (!string.IsNullOrEmpty(name))
                                                models.Add(name);
                                        }
                                    }
                                }
                
                                return models;
                            }
                            catch
                            {
                                return [];
                            }
                        }

                        public async Task<List<string>> GetLlamaCppModelsAsync()
                        {
                            try
                            {
                                var json = await _http.GetStringAsync(LlamaCppModelsUrl);
                                using var doc = JsonDocument.Parse(json);
                                var models = new List<string>();

                                if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                                    dataEl.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var model in dataEl.EnumerateArray())
                                    {
                                        AddModelId(models, model, "id");
                                    }
                                }

                                if (doc.RootElement.TryGetProperty("models", out var modelsEl) &&
                                    modelsEl.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var model in modelsEl.EnumerateArray())
                                    {
                                        AddModelId(models, model, "name");
                                        AddModelId(models, model, "id");
                                    }
                                }

                                return models;
                            }
                            catch
                            {
                                return [];
                            }
                        }

                        private static void AddModelId(List<string> models, JsonElement model, string propertyName)
                        {
                            if (!model.TryGetProperty(propertyName, out var valueEl)) return;

                            var value = valueEl.GetString();
                            if (!string.IsNullOrWhiteSpace(value) && !models.Contains(value))
                                models.Add(value);
                        }

                    }
                }
