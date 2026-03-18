namespace AIG.Game.Cosmos;

public sealed class OrbitParameters
{
    public OrbitParameters(double semiMajorAxis, double orbitalPeriodSeconds, double inclinationRadians = 0d, double initialPhaseRadians = 0d)
    {
        if (semiMajorAxis <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(semiMajorAxis));
        }

        if (orbitalPeriodSeconds <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(orbitalPeriodSeconds));
        }

        SemiMajorAxis = semiMajorAxis;
        OrbitalPeriodSeconds = orbitalPeriodSeconds;
        InclinationRadians = inclinationRadians;
        InitialPhaseRadians = initialPhaseRadians;
    }

    public double SemiMajorAxis { get; }
    public double OrbitalPeriodSeconds { get; }
    public double InclinationRadians { get; }
    public double InitialPhaseRadians { get; }
}
