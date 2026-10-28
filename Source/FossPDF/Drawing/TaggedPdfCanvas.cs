using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FossPDF.Infrastructure;
using SkiaSharp;

namespace FossPDF.Drawing
{
    /// <summary>
    /// Renders a document into independently addressable paint batches. Drawing is recorded into
    /// nested Skia pictures: each semantic element owns a picture, and the surrounding page content
    /// is split into artifact pictures at every semantic boundary. Every batch keeps the page
    /// coordinates of the content it contains, so the tagged-PDF backend can paint the fragments as
    /// form XObjects without changing the visual result.
    ///
    /// Pictures are converted into one-page PDF fragments as soon as they are complete. Skia cannot
    /// tag PDF content directly, so this canvas only captures; the structure tree is written by the
    /// PDFsharp-based companion package.
    /// </summary>
    internal sealed class TaggedPdfCanvas : SkiaCanvasBase, ISemanticCanvas
    {
        private sealed class Scope
        {
            public SemanticNode? Node { get; init; }
            public bool SuppressesSemantics { get; init; }
            public bool IsTransparent { get; init; }
            public SKCanvas? PreviousCanvas { get; init; }
            public SKPictureRecorder? Recorder { get; set; }
            public SKCanvas? Canvas { get; set; }
            public bool HasPaint { get; set; }
        }

        private sealed class PageCapture
        {
            public PageCapture(Size size)
            {
                Size = size;
            }

            public Size Size { get; }
            public List<TaggedPaintBatch> Batches { get; } = new List<TaggedPaintBatch>();
        }

        private readonly SemanticNode _root = new SemanticNode("Document", null, null)
        {
            Id = "n0",
            CreationOrder = 0
        };

        private readonly List<PageCapture> _pages = new List<PageCapture>();
        private readonly Stack<Scope> _scopes = new Stack<Scope>();
        private PageCapture? _page;
        private int _nextNodeId = 1;
        private int _nextNodeOrder = 1;
        private int _nextBatchOrder;
        private bool _disposed;

        internal TaggedDocumentContent BuildContent(DocumentMetadata metadata, PdfAConformance? pdfAConformance)
        {
            var pages = _pages
                .Select(page => new TaggedDocumentPage(
                    page.Size.Width,
                    page.Size.Height,
                    page.Batches.ToArray()))
                .ToArray();

            var root = BuildNode(_root);
            return new TaggedDocumentContent(metadata, pages, root, pages.Length, pdfAConformance);
        }

        private static TaggedStructureNode BuildNode(SemanticNode node)
        {
            var children = node.Children.Select(BuildNode).ToArray();
            var order = node.ContentOrder;

            foreach (var child in children)
                order = Math.Min(order, child.Order);

            if (order == int.MaxValue)
                order = node.CreationOrder;

            return new TaggedStructureNode(
                node.Id ?? throw new InvalidOperationException("A semantic node was not assigned an identifier."),
                node.Role,
                node.AlternativeText,
                node.Language,
                order,
                children);
        }

        public override void BeginDocument()
        {
        }

        public override void EndDocument()
        {
        }

        public override void BeginPage(Size size)
        {
            _page = new PageCapture(size);
            PushRecordingScope(null, suppressesSemantics: false);
        }

        public override void EndPage()
        {
            var page = _page;
            PopScope(finalize: true);

            if (page != null)
                _pages.Add(page);

            _page = null;
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            foreach (var scope in _scopes)
                scope.Recorder?.Dispose();

            _scopes.Clear();
            Canvas = null;
            _disposed = true;
        }

        public void BeginSemanticScope(SemanticNode node)
        {
            if (IsSuppressed())
            {
                PushTransparentScope();
                return;
            }

            if (node.Id == null)
            {
                node.Id = "n" + _nextNodeId;
                _nextNodeId++;
                node.CreationOrder = _nextNodeOrder;
                _nextNodeOrder++;
            }

            if (node.Parent == null)
            {
                var parent = _scopes
                    .Select(scope => scope.Node)
                    .FirstOrDefault(candidate => candidate != null) ?? _root;

                node.Parent = parent;
                parent.Children.Add(node);
            }

            PushRecordingScope(node, suppressesSemantics: false);
        }

        public void EndSemanticScope()
        {
            PopScope(finalize: true);
        }

        public void BeginArtifactScope()
        {
            if (IsSuppressed())
            {
                PushTransparentScope();
                return;
            }

            PushRecordingScope(null, suppressesSemantics: true);
        }

        public void EndArtifactScope()
        {
            PopScope(finalize: true);
        }

        internal override void EnableZIndexLayers()
        {
            throw new NotSupportedException(
                "ZIndex cannot be combined with tagged PDF generation because stacking layers cannot be addressable semantic batches.");
        }

        protected override void OnPaintOperation()
        {
            foreach (var scope in _scopes)
            {
                if (scope.Recorder == null)
                    continue;

                scope.HasPaint = true;
                return;
            }
        }

        private bool IsSuppressed()
        {
            return _scopes.Count > 0 && _scopes.Peek().SuppressesSemantics;
        }

        private void PushTransparentScope()
        {
            _scopes.Push(new Scope
            {
                IsTransparent = true,
                SuppressesSemantics = true,
                PreviousCanvas = Canvas
            });
        }

        private void PushRecordingScope(SemanticNode? node, bool suppressesSemantics)
        {
            var parent = _scopes.Count > 0 ? _scopes.Peek() : null;

            if (parent != null && !parent.IsTransparent)
                Checkpoint(parent);

            var pageSize = _page?.Size ?? Size.Zero;
            var matrix = Canvas?.TotalMatrix ?? SKMatrix.Identity;
            var clip = GetClip(Canvas);

            var scope = new Scope
            {
                Node = node,
                SuppressesSemantics = suppressesSemantics,
                PreviousCanvas = Canvas
            };

            StartRecording(scope, pageSize, matrix, clip);
            _scopes.Push(scope);
            Canvas = scope.Canvas;
        }

        private Scope PopScope(bool finalize)
        {
            var scope = _scopes.Pop();

            if (finalize)
                Complete(scope);

            Canvas = scope.PreviousCanvas;
            return scope;
        }

        /// <summary>
        /// Completes the current picture of a scope and starts a fresh one with the same initial
        /// state. Called whenever a nested scope starts so that artifacts and semantic content keep
        /// their exact paint order.
        /// </summary>
        private void Checkpoint(Scope scope)
        {
            if (scope.Recorder == null || scope.Canvas == null)
                return;

            var matrix = scope.Canvas.TotalMatrix;
            var clip = GetClip(scope.Canvas);
            var pageSize = _page?.Size ?? Size.Zero;

            Complete(scope);
            StartRecording(scope, pageSize, matrix, clip);

            if (ReferenceEquals(_scopes.Peek(), scope))
                Canvas = scope.Canvas;
        }

        private void StartRecording(Scope scope, Size pageSize, SKMatrix matrix, SKRect clip)
        {
            var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(0, 0, pageSize.Width, pageSize.Height));

            if (!clip.IsEmpty)
                canvas.ClipRect(clip, SKClipOperation.Intersect, antialias: false);

            if (!matrix.IsIdentity)
                canvas.SetMatrix(matrix);

            scope.Recorder = recorder;
            scope.Canvas = canvas;
            scope.HasPaint = false;
        }

        private void Complete(Scope scope)
        {
            if (scope.Recorder == null)
                return;

            var recorder = scope.Recorder;
            var picture = recorder.EndRecording();
            var hasPaint = scope.HasPaint;

            scope.Recorder = null;
            scope.Canvas = null;

            try
            {
                if (hasPaint)
                    AddBatch(scope.Node, picture);
            }
            finally
            {
                picture.Dispose();
                recorder.Dispose();
            }
        }

        private void AddBatch(SemanticNode? node, SKPicture picture)
        {
            var page = _page ?? throw new InvalidOperationException("A paint batch was completed outside a page.");
            var order = _nextBatchOrder;
            _nextBatchOrder++;

            if (node != null)
                node.ContentOrder = Math.Min(node.ContentOrder, order);

            page.Batches.Add(new TaggedPaintBatch(RenderFragment(picture, page.Size), node?.Id, order));
        }

        private static byte[] RenderFragment(SKPicture picture, Size pageSize)
        {
            using var stream = new MemoryStream();

            using (var document = SKDocument.CreatePdf(stream))
            {
                using var canvas = document.BeginPage(pageSize.Width, pageSize.Height);
                canvas.DrawPicture(picture);
                document.EndPage();
                document.Close();
            }

            return stream.ToArray();
        }

        private static SKRect GetClip(SKCanvas? canvas)
        {
            if (canvas == null)
                return SKRect.Empty;

            var bounds = canvas.DeviceClipBounds;

            if (bounds.Width <= 0 || bounds.Height <= 0)
                return SKRect.Empty;

            return new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        }
    }
}
