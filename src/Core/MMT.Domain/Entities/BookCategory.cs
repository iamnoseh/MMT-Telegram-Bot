using MMT.Domain.Common;

namespace MMT.Domain.Entities;


public class BookCategory : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public int Year { get; set; }
    public bool IsActive { get; set; } = true;
    
    public ICollection<Book> Books { get; set; } = new List<Book>();
}
