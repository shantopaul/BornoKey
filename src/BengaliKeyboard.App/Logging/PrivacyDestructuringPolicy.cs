using System.Diagnostics.CodeAnalysis;
using BengaliKeyboard.Core.Models;
using Serilog.Core;
using Serilog.Events;

namespace BengaliKeyboard.App.Logging;

/// <summary>
/// Enforces privacy-first logging by redacting sensitive user text properties from domain models
/// even if a developer logs an object using Serilog's {@Model} destructuring operator (SEC-04 / CHK-07).
/// </summary>
public sealed class PrivacyDestructuringPolicy : IDestructuringPolicy
{
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        switch (value)
        {
            case ClipboardEntry clip:
                result = new StructureValue(
                [
                    new LogEventProperty(nameof(ClipboardEntry.Id), new ScalarValue(clip.Id)),
                    new LogEventProperty(nameof(ClipboardEntry.IsPinned), new ScalarValue(clip.IsPinned)),
                    new LogEventProperty("ContentLength", new ScalarValue(clip.Content?.Length ?? 0)),
                    new LogEventProperty(nameof(ClipboardEntry.CreatedUtc), new ScalarValue(clip.CreatedUtc))
                ], nameof(ClipboardEntry));
                return true;

            case KeyDefinition key:
                result = new StructureValue(
                [
                    new LogEventProperty(nameof(KeyDefinition.Id), new ScalarValue(key.Id)),
                    new LogEventProperty(nameof(KeyDefinition.KeyType), new ScalarValue(key.KeyType)),
                    new LogEventProperty(nameof(KeyDefinition.RowIndex), new ScalarValue(key.RowIndex)),
                    new LogEventProperty(nameof(KeyDefinition.ColumnIndex), new ScalarValue(key.ColumnIndex)),
                    new LogEventProperty(nameof(KeyDefinition.WidthRatio), new ScalarValue(key.WidthRatio))
                ], nameof(KeyDefinition));
                return true;

            case SuggestionCandidate candidate:
                result = new StructureValue(
                [
                    new LogEventProperty(nameof(SuggestionCandidate.IsNextWordPrediction), new ScalarValue(candidate.IsNextWordPrediction)),
                    new LogEventProperty(nameof(SuggestionCandidate.Score), new ScalarValue(candidate.Score)),
                    new LogEventProperty(nameof(SuggestionCandidate.ReplacePrefixLength), new ScalarValue(candidate.ReplacePrefixLength)),
                    new LogEventProperty("WordLength", new ScalarValue(candidate.Word?.Length ?? 0))
                ], nameof(SuggestionCandidate));
                return true;

            default:
                result = null;
                return false;
        }
    }
}
