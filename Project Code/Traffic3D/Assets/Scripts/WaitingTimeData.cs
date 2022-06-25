using CsvHelper.Configuration.Attributes;

public class WaitingTimeData
{
    [Name("Time step")]
    [Index(0)]
    public int TimeStepProperty { get; set; }
    [Name("North Waiting Time")]
    [Index(1)]
    public double WaitingTimePropertyN { get; set; }
    [Name("East Waiting Time")]
    [Index(2)]
    public double WaitingTimePropertyE { get; set; }
    [Name("South Waiting Time")]
    [Index(3)]
    public double WaitingTimePropertyS { get; set; }
    [Name("West Waiting Time")]
    [Index(4)]
    public double WaitingTimePropertyW { get; set; }
    /*[Name("Total Waiting Time")]
    [Index(5)]
    public double WaitingTimePropertyTotal { get; set; }
    [Name("Average Waiting Time")]
    [Index(6)]
    public double WaitingTimePropertyAvg { get; set; }*/
}
