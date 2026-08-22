namespace LLMPicker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    public class SettingsManager
    {
        private readonly string _settingsPath;

        public SettingsManager(string settingsPath)
        {
            _settingsPath = settingsPath;
        }

        public string? GetLastModel(ProviderType providerType)
        {
            var key = GetSettingKey(providerType);
            return LoadSetting(key);
        }

        public void SaveLastModel(ProviderType providerType, string model)
        {
            var key = GetSettingKey(providerType);
            SaveSetting(key, model);
        }

        private static string GetSettingKey(ProviderType providerType) =>
            providerType switch
            {
                ProviderType.Ollama => "lastOllamaModel",
                ProviderType.FoundryLocal => "lastFoundryModel",
                ProviderType.LlamaCpp => "lastLlamaCppModel",
                _ => "lastModel"
            };

        private string? LoadSetting(string key)
        {
            try
            {
                if (!File.Exists(_settingsPath)) return null;
                var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
                return doc.RootElement.TryGetProperty(key, out var el) ? el.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        private void SaveSetting(string key, string value)
        {
            try
            {
                var settings = new Dictionary<string, string>();
                if (File.Exists(_settingsPath))
                {
                    try
                    {
                        var existing = JsonDocument.Parse(File.ReadAllText(_settingsPath));
                        foreach (var prop in existing.RootElement.EnumerateObject())
                            settings[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                    catch { }
                }
                settings[key] = value;
                File.WriteAllText(_settingsPath,
                    JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* non-critical */ }
        }
    }
}