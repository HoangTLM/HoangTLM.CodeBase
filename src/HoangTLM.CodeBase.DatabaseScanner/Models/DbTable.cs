using System;

namespace HoangTLM.CodeBase.DatabaseScanner.Models
{
    public class DbTable
    {
        public string Id { get; set; }
        public string ProjectId { get; set; }
        public string Name { get; set; }
        public string SchemaName { get; set; }
        public string DatabaseName { get; set; }
        public string Description { get; set; }
    }
}
