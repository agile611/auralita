using System.Text.Json;
using System.Xml.Linq;

namespace Auralita.Core.Plex;

internal sealed record PlexElement(
    string Name,
    IReadOnlyDictionary<string, string?> Attributes,
    IReadOnlyList<PlexElement> Children)
{
    public string? Attribute(string name) =>
        Attributes.GetValueOrDefault(name);

    public IEnumerable<PlexElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}

internal static class PlexResponseParser
{
    public static PlexElement Parse(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        var trimmed = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (trimmed.StartsWith('<'))
        {
            try
            {
                return ParseXml(XDocument.Parse(trimmed, LoadOptions.None).Root
                    ?? throw new PlexConnectionException("Plex returned an empty XML response."));
            }
            catch (System.Xml.XmlException exception)
            {
                throw new PlexConnectionException("Plex returned malformed XML.", exception);
            }
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("MediaContainer", out var container))
            {
                return ParseJson("MediaContainer", container);
            }

            return ParseJson(root.ValueKind == JsonValueKind.Array ? "Device" : "Root", root);
        }
        catch (JsonException exception)
        {
            throw new PlexConnectionException("Plex returned an invalid JSON response.", exception);
        }
    }

    private static PlexElement ParseXml(XElement element) =>
        new(
            element.Name.LocalName,
            element.Attributes().ToDictionary(
                attribute => attribute.Name.LocalName,
                attribute => (string?)attribute.Value,
                StringComparer.OrdinalIgnoreCase),
            element.Elements().Select(ParseXml).ToArray());

    private static PlexElement ParseJson(string name, JsonElement element)
    {
        var attributes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var children = new List<PlexElement>();
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (TryReadScalar(property.Value, out var scalar))
                    {
                        attributes[property.Name] = scalar;
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var values = property.Value.EnumerateArray().ToArray();
                        if (values.All(value => TryReadScalar(value, out _)))
                        {
                            attributes[property.Name] = string.Join(
                                ",",
                                values.Select(value => TryReadScalar(value, out var item) ? item : string.Empty));
                        }
                        else
                        {
                            children.AddRange(values.Select(value => ParseJson(property.Name, value)));
                        }
                    }
                    else
                    {
                        children.Add(ParseJson(property.Name, property.Value));
                    }
                }

                break;
            case JsonValueKind.Array:
                children.AddRange(element.EnumerateArray().Select(item => ParseJson(name, item)));
                break;
            default:
                TryReadScalar(element, out var value);
                attributes["value"] = value;
                break;
        }

        return new PlexElement(name, attributes, children);
    }

    private static bool TryReadScalar(JsonElement element, out string? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            JsonValueKind.Null => null,
            _ => null
        };
        return element.ValueKind is
            JsonValueKind.String or
            JsonValueKind.Number or
            JsonValueKind.True or
            JsonValueKind.False or
            JsonValueKind.Null;
    }
}
