using System.ComponentModel;

namespace FossPDF.Elements
{
    internal class DebugPointer : Container
    {
        public string Target { get; set; } = string.Empty;
        public bool Highlight { get; set; }
    }
}
