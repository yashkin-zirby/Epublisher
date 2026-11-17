using Epublisher.DataEntities;
using Epublisher.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Epublisher.Services
{
    public class SettingsManager
    {
        private readonly string _filePath;
        private readonly JsonSerializerOptions _options;
        public Dictionary<string, object> Settings { get; private set; }
        public string AppDataFolder {  get; private set; }

        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, object>))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JsonElement))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ThemeLocalization))]
        public SettingsManager(string baseFolder)
        {
            AppDataFolder = baseFolder;
            _filePath = Path.Combine(baseFolder, "settings.json");
            _options = new JsonSerializerOptions { WriteIndented = true };
            Settings = Load();
        }

        // Подавляем предупреждения линкера для динамического метода конвертации типов
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026", Justification = "Dynamic conversion is required for settings")]
        [UnconditionalSuppressMessage("Aot", "IL3050", Justification = "Dynamic conversion is required for settings")]
        public T? GetProperty<T>(string path, T? defaultValue)
        {
            string[] parts = path.Split('.');
            object current = Settings;

            for (int i = 0; i < parts.Length; i++)
            {
                if (current is Dictionary<string, object> dict && dict.TryGetValue(parts[i], out var value))
                {
                    if (i == parts.Length - 1)
                    {
                        return ConvertValue<T>(value, defaultValue);
                    }
                    current = value;
                }
                else
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public void SetProperty(string path, object value)
        {
            string[] parts = path.Split('.');
            Dictionary<string, object> current = Settings;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                if (!current.TryGetValue(part, out var next) || !(next is Dictionary<string, object>))
                {
                    var newDict = new Dictionary<string, object>();
                    current[part] = newDict;
                    current = newDict;
                }
                else
                {
                    current = (Dictionary<string, object>)next;
                }
            }

            current[parts[^1]] = value;
            Save();
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026")]
        [UnconditionalSuppressMessage("Aot", "IL3050")]
        private T? ConvertValue<T>(object value, T? defaultValue)
        {
            try
            {
                if (value is JsonElement element)
                {
                    return JsonSerializer.Deserialize<T>(element.GetRawText(), _options);
                }

                if (value is T typedValue)
                {
                    return typedValue;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch(Exception e)
            {
                ExceptionProcesser.Process(e);
                return defaultValue;
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026")]
        [UnconditionalSuppressMessage("Aot", "IL3050")]
        private Dictionary<string, object> Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var rawDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _options);

                    return rawDict != null ? NormalizeDictionary(rawDict) : new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            return new Dictionary<string, object>();
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026")]
        [UnconditionalSuppressMessage("Aot", "IL3050")]
        private Dictionary<string, object> NormalizeDictionary(Dictionary<string, object> source)
        {
            var result = new Dictionary<string, object>();
            foreach (var kvp in source)
            {
                if (kvp.Value is JsonElement element && element.ValueKind == JsonValueKind.Object)
                {
                    var subDict = JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText(), _options);
                    result[kvp.Key] = subDict != null ? NormalizeDictionary(subDict) : kvp.Value;
                }
                else
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026")]
        [UnconditionalSuppressMessage("Aot", "IL3050")]
        public void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(Settings, _options);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
