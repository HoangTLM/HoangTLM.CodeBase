using System;

namespace HoangTLM.CodeBase.DotNetScanner.Models
{
    public class CodeEntity
    {
        public string Id { get; set; }
        public string FileId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Signature { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Metadata { get; set; }
        public string Description { get; set; }
        public string RelativePath { get; set; }
        public string AbsolutePath { get; set; }
    }
}
