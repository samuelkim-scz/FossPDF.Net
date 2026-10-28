using System;
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace FossPDF.PdfOperations
{
    internal static class PdfAttachments
    {
        internal static void Apply(PdfDocument source, PdfDocument destination, IReadOnlyList<AttachmentInput> additions)
        {
            var files = Read(source);
            foreach (var attachment in additions)
            {
                if (files.ContainsKey(attachment.Key) && !attachment.Replace)
                    throw new PdfOperationException($"An attachment with key '{attachment.Key}' already exists. Set Replace to replace it.");
                files[attachment.Key] = attachment;
            }
            if (files.Count == 0)
                return;

            var names = new PdfArray(destination);
            var associated = new PdfArray(destination);
            foreach (var pair in files.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var attachment = pair.Value;
                var embedded = new PdfDictionary(destination);
                embedded.Elements.SetName("/Type", "/EmbeddedFile");
                embedded.Elements.SetName("/Subtype", "/" + attachment.MimeType);
                var parameters = new PdfDictionary(destination);
                parameters.Elements.SetInteger("/Size", attachment.Data.Length);
                if (attachment.CreationDate.HasValue)
                    parameters.Elements.SetValue("/CreationDate", new PdfDate(attachment.CreationDate.Value.ToUniversalTime()));
                if (attachment.ModificationDate.HasValue)
                    parameters.Elements.SetValue("/ModDate", new PdfDate(attachment.ModificationDate.Value.ToUniversalTime()));
                embedded.Elements.SetObject("/Params", parameters);
                PdfStreams.CreateCompressed(embedded, attachment.Data);
                destination.Internals.AddObject(embedded);

                var specification = new PdfDictionary(destination);
                specification.Elements.SetName("/Type", "/Filespec");
                specification.Elements.SetString("/F", new string(attachment.Name.Select(x => x <= 127 ? x : '_').ToArray()));
                specification.Elements.SetValue("/UF", Unicode(attachment.Name));
                if (attachment.Description != null)
                    specification.Elements.SetValue("/Desc", Unicode(attachment.Description));
                var content = new PdfDictionary(destination);
                content.Elements.SetReference("/F", embedded);
                content.Elements.SetReference("/UF", embedded);
                specification.Elements.SetObject("/EF", content);
                if (attachment.Relationship != null)
                    specification.Elements.SetName("/AFRelationship", "/" + attachment.Relationship);
                destination.Internals.AddObject(specification);
                names.Elements.Add(Unicode(pair.Key));
                names.Elements.Add(PdfInternals.GetReference(specification)!);
                if (attachment.Relationship != null)
                    associated.Elements.Add(PdfInternals.GetReference(specification)!);
            }
            var tree = new PdfDictionary(destination);
            tree.Elements.SetObject("/Names", names);
            var catalog = destination.Internals.Catalog;
            var nameDictionary = catalog.Elements.GetDictionary("/Names") ?? new PdfDictionary(destination);
            nameDictionary.Elements.SetObject("/EmbeddedFiles", tree);
            catalog.Elements.SetObject("/Names", nameDictionary);
            if (associated.Elements.Count > 0)
                catalog.Elements.SetObject("/AF", associated);
        }

        private static PdfString Unicode(string value) => new(value, PdfStringEncoding.Unicode);

        private static Dictionary<string, AttachmentInput> Read(PdfDocument source)
        {
            var result = new Dictionary<string, AttachmentInput>(StringComparer.Ordinal);
            var visited = new HashSet<PdfDictionary>();
            var specifications = new HashSet<PdfDictionary>();
            var catalog = source.Internals.Catalog;
            var names = catalog.Elements.GetDictionary("/Names");
            var tree = names?.Elements.GetDictionary("/EmbeddedFiles");
            if (tree != null)
                Visit(tree, 0);
            var associated = Array(catalog.Elements.GetValue("/AF"));
            if (associated != null)
            {
                foreach (var item in associated.Elements)
                {
                    var specification = Dictionary(item) ?? throw new PdfOperationException("An associated-file entry is malformed.");
                    if (specifications.Contains(specification))
                        continue;
                    var name = specification.Elements.GetString("/UF");
                    if (string.IsNullOrEmpty(name))
                        name = specification.Elements.GetString("/F");
                    Add(name, specification);
                }
            }
            return result;

            void Visit(PdfDictionary node, int depth)
            {
                if (depth > 32 || !visited.Add(node))
                    throw new PdfOperationException("The embedded-file name tree is cyclic or too deeply nested.");
                var entries = Array(node.Elements.GetValue("/Names"));
                if (entries != null)
                {
                    if (entries.Elements.Count % 2 != 0)
                        throw new PdfOperationException("The embedded-file name tree contains an incomplete entry.");
                    for (var index = 0; index < entries.Elements.Count; index += 2)
                    {
                        var key = Resolve(entries.Elements[index]) as PdfString;
                        var specification = Dictionary(entries.Elements[index + 1]);
                        if (key == null || specification == null)
                            throw new PdfOperationException("The embedded-file name tree contains an invalid entry.");
                        Add(key.Value, specification);
                    }
                }
                var children = Array(node.Elements.GetValue("/Kids"));
                if (children != null)
                    foreach (var child in children.Elements)
                        Visit(Dictionary(child) ?? throw new PdfOperationException("The embedded-file name tree has an invalid child."), depth + 1);
            }

            void Add(string key, PdfDictionary specification)
            {
                if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
                    throw new PdfOperationException("The input contains missing or duplicate attachment keys.");
                var embeddedFiles = specification.Elements.GetDictionary("/EF");
                var embedded = embeddedFiles?.Elements.GetDictionary("/UF") ?? embeddedFiles?.Elements.GetDictionary("/F");
                if (embedded?.Stream == null)
                    throw new PdfOperationException("An attachment has no embedded file stream.");
                var data = PdfXmpMetadata.ReadStream(embedded);
                var name = specification.Elements.GetString("/UF");
                if (string.IsNullOrEmpty(name))
                    name = specification.Elements.GetString("/F");
                if (string.IsNullOrEmpty(name))
                    name = key;
                var mime = embedded.Elements.GetName("/Subtype").TrimStart('/');
                var relationship = specification.Elements.GetName("/AFRelationship").TrimStart('/');
                var parameters = embedded.Elements.GetDictionary("/Params");
                DateTime? Date(string field) => parameters?.Elements.ContainsKey(field) == true
                    ? parameters.Elements.GetDateTime(field, DateTime.MinValue) : null;
                result.Add(key, new AttachmentInput(data, key, name,
                    string.IsNullOrEmpty(mime) ? "application/octet-stream" : mime,
                    specification.Elements.ContainsKey("/Desc") ? specification.Elements.GetString("/Desc") : null,
                    Date("/CreationDate"), Date("/ModDate"), false,
                    string.IsNullOrEmpty(relationship) ? null : relationship));
                specifications.Add(specification);
            }
        }

        private static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;
        private static PdfDictionary? Dictionary(PdfItem? item) => Resolve(item) as PdfDictionary;
        private static PdfArray? Array(PdfItem? item) => Resolve(item) as PdfArray;
    }
}
