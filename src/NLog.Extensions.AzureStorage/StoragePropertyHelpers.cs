using System;
using System.Collections.Generic;
using NLog.Common;
using NLog.Targets;

namespace NLog.Extensions.AzureStorage
{
    internal static class StoragePropertyHelpers
    {
        /// <summary>
        /// Renders blob/queue metadata (or blob tags) from the log event that creates the blob/queue.
        /// </summary>
        /// <returns>Rendered properties, or null when there are none</returns>
        internal static IDictionary<string, string> RenderProperties(IList<TargetPropertyWithContext> properties, LogEventInfo logEvent, Func<string, string> sanitizeValue)
        {
            if (properties?.Count > 0)
            {
                var renderedProperties = new Dictionary<string, string>();
                foreach (var property in properties)
                {
                    if (string.IsNullOrWhiteSpace(property.Name))
                        continue;

                    var propertyValue = property.Layout?.Render(logEvent);
                    if (!property.IncludeEmptyValue && string.IsNullOrEmpty(propertyValue))
                        continue;

                    propertyValue = propertyValue ?? string.Empty;
                    var sanitizedValue = sanitizeValue(propertyValue);
                    if (sanitizedValue != propertyValue)
                        InternalLogger.Debug("AzureStorage: Repaired metadata/tag {0} value: '{1}' -> '{2}'", property.Name, propertyValue, sanitizedValue);
                    renderedProperties[property.Name.Trim()] = sanitizedValue;
                }

                if (renderedProperties.Count > 0)
                    return renderedProperties;
            }

            return null;
        }

        // Metadata values travel as HTTP headers, so the SDK refuses non-ASCII or control chars outright, and
        // leading/trailing whitespace is stripped from the sent header after signing (-> 403 auth failure).
        private static bool IsValidMetadataValueChar(char c) => c >= 0x20 && c < 0x7F;
        internal static string SanitizeMetadataValue(string value) => ReplaceInvalidChars(value.Trim(), IsValidMetadataValueChar, int.MaxValue);

        /// <summary>
        /// Replaces chars Azure rejects with '_' and truncates to <paramref name="maxLength"/>, since a rejected
        /// value fails the whole request (losing the log batch).
        /// </summary>
        internal static string ReplaceInvalidChars(string value, Func<char, bool> isValidChar, int maxLength)
        {
            // Only the first maxLength chars survive, so never scan or allocate past the cap.
            int length = Math.Min(value.Length, maxLength);
            char[] buffer = null;
            for (int i = 0; i < length; ++i)
            {
                if (!isValidChar(value[i]))
                {
                    buffer = buffer ?? value.ToCharArray(0, length);
                    buffer[i] = '_';
                }
            }

            if (buffer != null)
                return new string(buffer);
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }
    }
}
