using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace FossPDF.Svg
{
    /// <summary>
    /// Parses and validates SVG input before it reaches Svg.Skia.
    /// The validator keeps SVG documents self-contained: external references, scripts and DTDs are
    /// rejected so rendering cannot read the network, the file system or arbitrary local files.
    /// </summary>
    internal static class SvgInputValidator
    {
        internal const int MaxInputSizeBytes = 32 * 1024 * 1024;

        private const string SvgNamespace = "http://www.w3.org/2000/svg";

        private static readonly XmlReaderSettings ReaderSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxInputSizeBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = false
        };

        internal static XDocument Parse(Stream stream)
        {
            try
            {
                using var reader = XmlReader.Create(stream, ReaderSettings);
                var document = XDocument.Load(reader, LoadOptions.None);
                Validate(document);

                return document;
            }
            catch (SvgParseException)
            {
                throw;
            }
            catch (XmlException exception)
            {
                throw new SvgParseException("The SVG input is not well-formed XML.", exception);
            }
        }

        internal static XDocument Parse(string content)
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(content), ReaderSettings);
                var document = XDocument.Load(reader, LoadOptions.None);
                Validate(document);

                return document;
            }
            catch (SvgParseException)
            {
                throw;
            }
            catch (XmlException exception)
            {
                throw new SvgParseException("The SVG input is not well-formed XML.", exception);
            }
        }

        internal static void Validate(XDocument document)
        {
            var root = document.Root
                ?? throw new SvgParseException("The SVG input has no root element.");

            if (root.Name.LocalName != "svg" || root.Name.NamespaceName != SvgNamespace)
            {
                throw new SvgParseException(
                    $"The SVG root element must be <svg> in the '{SvgNamespace}' namespace.");
            }

            ValidateIntrinsicSize(root);

            foreach (var instruction in document.Nodes().OfType<XProcessingInstruction>())
            {
                if (string.Equals(instruction.Target, "xml-stylesheet", StringComparison.OrdinalIgnoreCase))
                    throw new SvgParseException("External stylesheets declared with xml-stylesheet are not supported.");
            }

            foreach (var element in root.DescendantsAndSelf())
            {
                if (string.Equals(element.Name.LocalName, "script", StringComparison.OrdinalIgnoreCase))
                    throw new SvgParseException("SVG <script> elements are not supported.");

                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                        continue;

                    var name = attribute.Name.LocalName;

                    if (string.Equals(name, "href", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "src", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateReference(element, name, attribute.Value);
                    }
                    else if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateCss(element, attribute.Value);
                        ValidateUrlFunctions(element, attribute.Value);
                    }
                    else if (attribute.Value.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        ValidateUrlFunctions(element, attribute.Value);
                    }
                }

                if (string.Equals(element.Name.LocalName, "style", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateCss(element, element.Value);
                    ValidateUrlFunctions(element, element.Value);
                }
            }
        }

        private static void ValidateIntrinsicSize(XElement root)
        {
            if (HasPositiveViewBox(root.Attribute("viewBox")?.Value))
                return;

            if (HasPositiveLength(root.Attribute("width")?.Value) &&
                HasPositiveLength(root.Attribute("height")?.Value))
            {
                return;
            }

            throw new SvgParseException(
                "The SVG document must declare a positive viewBox or absolute width and height " +
                "attributes so its intrinsic size is defined.");
        }

        private static bool HasPositiveViewBox(string? value)
        {
            if (value == null)
                return false;

            var parts = value.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            return parts.Length == 4 &&
                   TryParsePositiveNumber(parts[2], out _) &&
                   TryParsePositiveNumber(parts[3], out _);
        }

        private static bool HasPositiveLength(string? value)
        {
            if (value == null)
                return false;

            var text = value.Trim();

            // Percentages need a viewport that an embedded image does not have; a viewBox is the
            // way to give such a document an intrinsic size.
            if (text.EndsWith("%", StringComparison.Ordinal))
                return false;

            var number = text;

            // Allow the absolute units accepted by SVG for root width/height. The renderer resolves
            // the unit; the value itself only needs to be positive.
            foreach (var unit in new[] { "px", "pt", "pc", "mm", "cm", "in" })
            {
                if (text.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                {
                    number = text.Substring(0, text.Length - unit.Length).Trim();
                    break;
                }
            }

            return TryParsePositiveNumber(number, out _);
        }

        private static bool TryParsePositiveNumber(string value, out float number)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                   number > 0 &&
                   !float.IsNaN(number) &&
                   !float.IsInfinity(number);
        }

        private static void ValidateReference(XElement element, string attributeName, string value)
        {
            var reference = value.Trim();

            if (reference.Length == 0 ||
                reference[0] == '#' ||
                reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new SvgParseException(
                $"The <{element.Name.LocalName}> element references an external resource through " +
                $"'{attributeName}' ({Describe(reference)}). Only fragment ('#id') and 'data:' " +
                "references are accepted; external resource loading is disabled.");
        }

        private static void ValidateUrlFunctions(XElement element, string value)
        {
            var index = 0;

            while ((index = value.IndexOf("url(", index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var start = index + 4;
                var end = value.IndexOf(')', start);

                if (end < 0)
                {
                    throw new SvgParseException(
                        $"The <{element.Name.LocalName}> element contains an unterminated url() reference.");
                }

                var reference = value.Substring(start, end - start).Trim().Trim('\'', '"').Trim();

                if (!reference.StartsWith("#", StringComparison.Ordinal))
                {
                    throw new SvgParseException(
                        $"The <{element.Name.LocalName}> element references an external resource with " +
                        $"url({Describe(reference)}). Only local url(#id) references are accepted; " +
                        "external resource loading is disabled.");
                }

                index = end + 1;
            }
        }

        private static void ValidateCss(XElement element, string value)
        {
            if (value.IndexOf("@import", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new SvgParseException(
                    $"The <{element.Name.LocalName}> element imports an external stylesheet with @import, " +
                    "which is not supported.");
            }

            if (value.IndexOf("javascript:", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new SvgParseException(
                    $"The <{element.Name.LocalName}> element contains a javascript: URL, which is not supported.");
            }

            if (value.IndexOf("expression(", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new SvgParseException(
                    $"The <{element.Name.LocalName}> element contains a CSS expression(), which is not supported.");
            }
        }

        private static string Describe(string value)
        {
            const int maximumLength = 80;

            return value.Length <= maximumLength
                ? "'" + value + "'"
                : "'" + value.Substring(0, maximumLength) + "...'";
        }
    }
}
