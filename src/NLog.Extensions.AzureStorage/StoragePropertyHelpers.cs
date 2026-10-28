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
        /// <returns>Rendered properties (empty when there are none)</returns>
        internal static IDictionary<string, string> RenderProperties(IList<TargetPropertyWithContext> properties, LogEventInfo logEvent, Func<string, string> sanitizeValue, string targetType, string targetName)
        {
            var renderedProperties = new Dictionary<string, string>();
            if (properties == null)
                return renderedProperties;

            foreach (var property in properties)
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                    continue;

                var propertyValue = property.Layout?.Render(logEvent) ?? string.Empty;
                var sanitizedValue = sanitizeValue(propertyValue);    // before the empty check: trimming can empty a whitespace-only value
                if (!property.IncludeEmptyValue && sanitizedValue.Length == 0)
                    continue;

                if (sanitizedValue != propertyValue)
                    InternalLogger.Debug("{0}(Name={1}): Repaired metadata/tag {2} value: '{3}' -> '{4}'", targetType, targetName, property.Name, propertyValue, sanitizedValue);
                renderedProperties[property.Name.Trim()] = sanitizedValue;
            }

            return renderedProperties;
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
