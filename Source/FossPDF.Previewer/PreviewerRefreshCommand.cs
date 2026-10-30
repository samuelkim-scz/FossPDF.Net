namespace FossPDF.Previewer;

internal class DocumentSnapshot
{
    public InspectionSnapshot? Inspection { get; set; }
    public ICollection<PageSnapshot> Pages { get; set; } = new List<PageSnapshot>();

    public class PageSnapshot
    {
        public string Id { get; set; } = string.Empty;
        
        public float Width { get; set; }
        public float Height { get; set; }
        
    }
}
