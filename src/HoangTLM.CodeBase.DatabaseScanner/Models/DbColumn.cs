using System;

namespace HoangTLM.CodeBase.DatabaseScanner.Models
{
    public class DbColumn
    {
        public string Id { get; set; }
        public string TableId { get; set; }
        public string Name { get; set; }
        public string DataType { get; set; }
        public int? MaxLength { get; set; }
        public int IsNullable { get; set; }
        public int IsPrimaryKey { get; set; }
        public int IsForeignKey { get; set; }
        public string FkTable { get; set; }
        public string FkColumn { get; set; }
        public string DefaultVal { get; set; }
        public string Description { get; set; }

        // Transient properties for mapping
        public string TableName { get; set; }
        public string SchemaName { get; set; }
    }
}
