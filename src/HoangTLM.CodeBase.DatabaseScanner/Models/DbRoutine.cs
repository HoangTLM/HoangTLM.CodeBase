using System;

namespace HoangTLM.CodeBase.DatabaseScanner.Models
{
    public class DbRoutine
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string SchemaName { get; set; }
        public string Definition { get; set; }
        public string Description { get; set; }
    }
}
