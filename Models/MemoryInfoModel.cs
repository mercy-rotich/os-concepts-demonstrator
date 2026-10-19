namespace OS_Concepts_Demonstrator.Models;

    public class MemoryInfoModel
    {
        public double TotalGb { get; set; } 

        public double AvailableGb { get; set; } 

        public double UsedGb { get; set; } 

        public double UsedPercentage { get; set; }
    }