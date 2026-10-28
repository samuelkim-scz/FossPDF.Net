# Complex-script test fonts

FP-109 needs reproducible Arabic, Hebrew, Devanagari, CJK, emoji and combining-mark shaping
without depending on fonts installed on the machine running the tests. The files in this directory
are subsets of the Noto fonts published by the Google Fonts repository. They are embedded in
`FossPDF.UnitTests.dll` and registered under stable custom family names so a system-installed Noto
font cannot change the result.

| File | Source | Custom family name |
| --- | --- | --- |
| `FossPDFTest-Arabic.ttf` | `google/fonts` `ofl/notosansarabic` `NotoSansArabic[wdth,wght].ttf` | `FossPDF Test Arabic` |
| `FossPDFTest-Hebrew.ttf` | `google/fonts` `ofl/notosanshebrew` `NotoSansHebrew[wdth,wght].ttf` | `FossPDF Test Hebrew` |
| `FossPDFTest-Devanagari.ttf` | `google/fonts` `ofl/notosansdevanagari` `NotoSansDevanagari[wdth,wght].ttf` | `FossPDF Test Devanagari` |
| `FossPDFTest-CJK.ttf` | `google/fonts` `ofl/notosansjp` `NotoSansJP[wght].ttf` | `FossPDF Test CJK` |
| `FossPDFTest-Emoji.ttf` | `google/fonts` `ofl/notoemoji` `NotoEmoji[wght].ttf` | `FossPDF Test Emoji` |
| `FossPDFTest-Latin.ttf` | `google/fonts` `ofl/notosans` `NotoSans[wdth,wght].ttf` | `FossPDF Test Latin` |

Sources were downloaded from `https://raw.githubusercontent.com/google/fonts/main/ofl/...` on
October 27, 2026, instantiated at weight 400 (width 100 where present), and subset with
`fonttools` 4.67.0:

```text
pyftsubset <source>.ttf \
  --unicodes=<ranges used by the corpus> \
  --text=<characters used by the corpus> \
  --layout-features='*' --glyph-names --notdef-outline --recommended-glyphs \
  --name-IDs='*' --name-legacy --name-languages='*' --no-hinting
```

`layout-features='*'` keeps the GSUB/GPOS tables that implement Arabic contextual forms and
lam-alef ligatures, Hebrew niqqud positioning, Devanagari reordering, and emoji ZWJ sequences.
The emoji subset drops `COLR`/`CPAL`, so it is a monochrome outline font; colour emoji rendering
is out of scope. The subsets keep the original family names in their name tables (none of the
subsets uses the `Source` reserved font name of the Source Han Sans license).

The fonts are licensed under the SIL Open Font License 1.1. The license text for each family is
stored next to the font (`OFL-<family>.txt`) and is redistributed with the test binaries' source.
