using System;

namespace HoangTLM.CodeBase.DatabaseScanner.Models
{
    public class Project
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public string Language { get; set; }
        public DateTime ScannedAt { get; set; }
        public string Description { get; set; }
    }
}
