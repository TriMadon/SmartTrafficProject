using System;
using Accord.Fuzzy;

public class FuzzyEngine
{
    private InferenceSystem _inferenceSystem;

    public void Initialize()
    {
        FuzzySet fsVeryLowDemand = new FuzzySet("VeryLowDemand",
            new TrapezoidalFunction(3, 6, TrapezoidalFunction.EdgeType.Right));
        FuzzySet fsLowDemand = new FuzzySet("LowDemand", new TrapezoidalFunction(4, 7, 10));
        FuzzySet fsModerateDemand = new FuzzySet("ModerateDemand", new TrapezoidalFunction(8, 11, 14));
        FuzzySet fsHighDemand = new FuzzySet("HighDemand", new TrapezoidalFunction(12, 15, 18));
        FuzzySet fsVeryHighDemand = new FuzzySet("VeryHighDemand",
            new TrapezoidalFunction(16, 19, TrapezoidalFunction.EdgeType.Left));
        // Input: number of cars on a phase (traffic density)
        // Every linguistic variable needs a linguistic value(s)
        LinguisticVariable trafficDensity = new LinguisticVariable("DensityOfTraffic", 1, 21);
        trafficDensity.AddLabel(fsVeryLowDemand);
        trafficDensity.AddLabel(fsLowDemand);
        trafficDensity.AddLabel(fsModerateDemand);
        trafficDensity.AddLabel(fsHighDemand);
        trafficDensity.AddLabel(fsVeryHighDemand);

        FuzzySet fsVeryShort = new FuzzySet("VeryShort", new TrapezoidalFunction(8, 9, 10));
        FuzzySet fsShort = new FuzzySet("Short", new TrapezoidalFunction(9, 12, 16));
        FuzzySet fsMiddle = new FuzzySet("Moderate", new TrapezoidalFunction(14, 18, 22));
        FuzzySet fsLong = new FuzzySet("Long", new TrapezoidalFunction(20, 24, 28));
        FuzzySet fsVeryLong = new FuzzySet("VeryLong", new TrapezoidalFunction(26, 29, 31));

        // Output: Green Time
        LinguisticVariable greenTime = new LinguisticVariable("GreenTime", 0, 40);
        greenTime.AddLabel(fsVeryShort);
        greenTime.AddLabel(fsShort);
        greenTime.AddLabel(fsMiddle);
        greenTime.AddLabel(fsLong);
        greenTime.AddLabel(fsVeryLong);

        Database fuzzyDB = new Database();
        fuzzyDB.AddVariable(trafficDensity);
        fuzzyDB.AddVariable(greenTime);

        _inferenceSystem = new InferenceSystem(fuzzyDB, new CentroidDefuzzifier(1000));

        _inferenceSystem.NewRule("Rule 1", "IF DensityOfTraffic IS VeryLowDemand THEN GreenTime IS VeryShort");
        _inferenceSystem.NewRule("Rule 2", "IF DensityOfTraffic IS LowDemand THEN GreenTime IS Short");
        _inferenceSystem.NewRule("Rule 3", "IF DensityOfTraffic IS ModerateDemand THEN GreenTime IS Moderate");
        _inferenceSystem.NewRule("Rule 4", "IF DensityOfTraffic IS HighDemand THEN GreenTime IS Long");
        _inferenceSystem.NewRule("Rule 5", "IF DensityOfTraffic IS VeryHighDemand THEN GreenTime IS VeryLong");
    }

    public double DoInference(int numOfCars)
    {
        _inferenceSystem.SetInput("DensityOfTraffic", numOfCars);

        try
        {
            return _inferenceSystem.Evaluate("GreenTime");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
