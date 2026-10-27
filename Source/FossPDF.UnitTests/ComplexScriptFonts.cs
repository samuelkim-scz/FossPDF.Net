using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FossPDF.Drawing;

namespace FossPDF.UnitTests
{
    /// <summary>
    /// Registers the subset Noto fonts used by the complex-script corpus under stable family names.
    /// Registration is global (mirroring how consumers call <see cref="FontManager"/>), so it is
    /// performed once per test process.
    /// </summary>
    internal static class ComplexScriptFonts
    {
        public const string Latin = "FossPDF Test Latin";
        public const string Arabic = "FossPDF Test Arabic";
        public const string Hebrew = "FossPDF Test Hebrew";
        public const string Devanagari = "FossPDF Test Devanagari";
        public const string Cjk = "FossPDF Test CJK";
        public const string Emoji = "FossPDF Test Emoji";

        private static readonly object Sync = new();
        private static bool Registered { get; set; }

        private static readonly IReadOnlyList<(string Family, string FileName)> Fonts =
            new List<(string, string)>
            {
                (Latin, "FossPDFTest-Latin.ttf"),
                (Arabic, "FossPDFTest-Arabic.ttf"),
                (Hebrew, "FossPDFTest-Hebrew.ttf"),
                (Devanagari, "FossPDFTest-Devanagari.ttf"),
                (Cjk, "FossPDFTest-CJK.ttf"),
                (Emoji, "FossPDFTest-Emoji.ttf")
            };

        public static void Register()
        {
            lock (Sync)
            {
                if (Registered)
                    return;

                foreach (var (family, fileName) in Fonts)
                {
                    var resourceName = $"FossPDF.UnitTests.Resources.ComplexScripts.{fileName}";

                    using var stream = typeof(ComplexScriptFonts).GetTypeInfo().Assembly
                        .GetManifestResourceStream(resourceName)
                        ?? throw new InvalidOperationException($"Embedded test font '{resourceName}' was not found.");

                    FontManager.RegisterFontWithCustomName(family, stream);
                }

                Registered = true;
            }
        }
    }
}
