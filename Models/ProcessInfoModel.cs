namespace OS_Concepts_Demonstrator.Models;

    public class ProcessInfoModel
    {
        public int ProcessId { get; set; }

        public string Name { get; set; } = string.Empty;

        public double MemoryUsageMb { get; set; }
    }