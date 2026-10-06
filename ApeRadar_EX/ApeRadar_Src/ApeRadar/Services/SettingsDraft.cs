using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;

namespace ApeRadar.Services
{
    internal sealed class SettingsSnapshot
    {
        private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);

        internal SettingsSnapshot(ApplicationSettingsBase settings)
        {
            foreach (SettingsProperty property in settings.Properties)
            {
                if (property.Attributes[typeof(UserScopedSettingAttribute)] is UserScopedSettingAttribute)
                {
                    values.Add(property.Name, settings[property.Name]);
                }
            }
        }

        internal void Restore(ApplicationSettingsBase settings, IEnumerable<string> names)
        {
            foreach (string name in names)
            {
                settings[name] = values[name];
            }
        }
    }

    internal sealed class SettingsDraft
    {
        private readonly ApplicationSettingsBase settings;
        private readonly Dictionary<string, object?> changes = new(StringComparer.Ordinal);

        internal SettingsDraft(ApplicationSettingsBase settings)
        {
            this.settings = settings;
        }

        internal void Set<T>(string name, T value)
        {
            SettingsProperty property = settings.Properties[name]
                ?? throw new ArgumentException($"Unknown setting: {name}", nameof(name));
            if (property.Attributes[typeof(UserScopedSettingAttribute)] is not UserScopedSettingAttribute)
            {
                throw new ArgumentException($"Setting is not user-scoped: {name}", nameof(name));
            }
            changes[name] = value;
        }

        internal T Default<T>(string name)
        {
            object? value = settings.Properties[name]?.DefaultValue;
            if (value is T typed) return typed;
            string text = value?.ToString() ?? "";
            if (typeof(T) == typeof(string)) return (T)(object)text;
            return (T)(TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(text)
                ?? throw new InvalidOperationException($"Setting {name} has no default value."));
        }

        internal void Cancel() => changes.Clear();

        internal void Commit(Action persist)
        {
            SettingsSnapshot before = new(settings);
            try
            {
                foreach ((string name, object? value) in changes)
                {
                    settings[name] = value;
                }
                persist();
                changes.Clear();
            }
            catch
            {
                before.Restore(settings, changes.Keys);
                throw;
            }
        }
    }
}
