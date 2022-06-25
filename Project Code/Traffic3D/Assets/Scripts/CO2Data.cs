using CsvHelper.Configuration.Attributes;

public class Co2Data
{
    [Name("Time step")]
    [Index(0)]
    public int TimeStepProperty { get; set; }
    [Name("North CO2 Emission")]
    [Index(1)]
    public double Co2PropertyN { get; set; }
    [Name("East CO2 Emission")]
    [Index(2)]
    public double Co2PropertyE { get; set; }
    [Name("South CO2 Emission")]
    [Index(3)]
    public double Co2PropertyS { get; set; }
    [Name("West CO2 Emission")]
    [Index(4)]
    public double Co2PropertyW { get; set; }
}
