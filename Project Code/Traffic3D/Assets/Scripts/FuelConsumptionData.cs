using CsvHelper.Configuration.Attributes;

public class FuelConsumptionData
{
    [Name("Time step")]
    [Index(0)]
    public int TimeStepProperty { get; set; }
    [Name("North Fuel Consumption")]
    [Index(1)]
    public double FuelPropertyN { get; set; }
    [Name("East Fuel Consumption")]
    [Index(2)]
    public double FuelPropertyE { get; set; }
    [Name("South Fuel Consumption")]
    [Index(3)]
    public double FuelPropertyS { get; set; }
    [Name("West Fuel Consumption")]
    [Index(4)]
    public double FuelPropertyW { get; set; }
}
