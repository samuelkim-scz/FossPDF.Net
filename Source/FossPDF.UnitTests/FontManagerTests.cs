using System;
using System.IO;
using NUnit.Framework;
using FossPDF.Drawing;

namespace FossPDF.UnitTests
{
    public class FontManagerTests
    {
        [Test]
        public void LoadFontFromFile()
        {
            using var stream = File.OpenRead("Resources/FontContent.ttf"); 
            FontManager.RegisterFont(stream);
        }
        
        [Test]
        public void LoadFontFromEmbeddedResource()
        {
            FontManager.RegisterFontFromEmbeddedResource("FossPDF.UnitTests.Resources.FontEmbeddedResource.ttf");
        }

        [Test]
        public void LoadFontFromExplicitResourceAssembly()
        {
            FontManager.RegisterFontFromEmbeddedResource("FossPDF.UnitTests.Resources.FontEmbeddedResource.ttf", typeof(FontManagerTests).Assembly);
        }

        [Test]
        public void LoadFontFromExplicitResourceAssembly_RejectsNullAssembly()
        {
            Assert.Throws<ArgumentNullException>(() => FontManager.RegisterFontFromEmbeddedResource("font.ttf", null));
        }

        [Test]
        public void LoadDocumentSpecificFontFromExplicitResourceAssembly()
        {
            var manager = new DocumentSpecificFontManager();
            try
            {
                manager.RegisterFontFromEmbeddedResource("FossPDF.UnitTests.Resources.FontEmbeddedResource.ttf", typeof(FontManagerTests).Assembly);
                Assert.That(manager.StyleSets, Is.Not.Empty);
            }
            finally { manager.ClearCacheReadyForSubsets(); }
        }

        [Test]
        public void LoadDocumentSpecificFontFromCallingAssembly()
        {
            var manager = new DocumentSpecificFontManager();
            try
            {
                manager.RegisterFontFromEmbeddedResource("FossPDF.UnitTests.Resources.FontEmbeddedResource.ttf");
                Assert.That(manager.StyleSets, Is.Not.Empty);
            }
            finally { manager.ClearCacheReadyForSubsets(); }
        }

        [Test]
        public void LoadDocumentSpecificFontFromExplicitResourceAssembly_RejectsMissingResource()
        {
            Assert.Throws<ArgumentException>(() => new DocumentSpecificFontManager()
                .RegisterFontFromEmbeddedResource("missing.ttf", typeof(FontManagerTests).Assembly));
        }

        [Test]
        public void LoadDocumentSpecificFontFromExplicitResourceAssembly_RejectsNullAssembly()
        {
            Assert.Throws<ArgumentNullException>(() => new DocumentSpecificFontManager().RegisterFontFromEmbeddedResource("font.ttf", null));
        }
        
        [Test]
        public void LoadFontFromEmbeddedResource_ShouldThrowException_WhenResourceIsNotAvailable()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                FontManager.RegisterFontFromEmbeddedResource("FossPDF.UnitTests.WrongPath.ttf");
            });
        }
    }
}
