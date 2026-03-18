namespace AIG.Game.Cosmos;

public sealed class CelestialBody
{
    private const double Tau = Math.PI * 2d;
    private readonly List<CelestialBody> _children = [];

    public CelestialBody(
        string name,
        CelestialBodyKind kind,
        double mass,
        double radius,
        CelestialBody? parent = null,
        OrbitParameters? orbit = null,
        double rotationPeriodSeconds = 0d,
        double initialRotationRadians = 0d)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be provided.", nameof(name));
        }

        if (mass <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(mass));
        }

        if (radius <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        if ((parent is null) != (orbit is null))
        {
            throw new ArgumentException("Parent and orbit must be specified together for orbiting bodies.");
        }

        Name = name;
        Kind = kind;
        Mass = mass;
        Radius = radius;
        Parent = parent;
        Orbit = orbit;
        RotationPeriodSeconds = rotationPeriodSeconds;
        InitialRotationRadians = initialRotationRadians;

        parent?._children.Add(this);
    }

    public string Name { get; }
    public CelestialBodyKind Kind { get; }
    public double Mass { get; }
    public double Radius { get; }
    public CelestialBody? Parent { get; }
    public OrbitParameters? Orbit { get; }
    public double RotationPeriodSeconds { get; }
    public double InitialRotationRadians { get; }
    public IReadOnlyList<CelestialBody> Children => _children;

    public Vector3d GetAbsolutePosition(double simulationTimeSeconds, Vector3d systemOrigin)
    {
        if (Parent is null || Orbit is null)
        {
            return systemOrigin;
        }

        return Parent.GetAbsolutePosition(simulationTimeSeconds, systemOrigin) + GetRelativeOrbitPosition(simulationTimeSeconds);
    }

    public double GetRotationAngle(double simulationTimeSeconds)
    {
        if (RotationPeriodSeconds <= 0d)
        {
            return NormalizeAngle(InitialRotationRadians);
        }

        var turns = simulationTimeSeconds / RotationPeriodSeconds;
        return NormalizeAngle(InitialRotationRadians + turns * Tau);
    }

    private Vector3d GetRelativeOrbitPosition(double simulationTimeSeconds)
    {
        if (Orbit is null)
        {
            return Vector3d.Zero;
        }

        var phase = Orbit.InitialPhaseRadians + simulationTimeSeconds / Orbit.OrbitalPeriodSeconds * Tau;
        var planar = new Vector3d(
            Math.Cos(phase) * Orbit.SemiMajorAxis,
            0d,
            Math.Sin(phase) * Orbit.SemiMajorAxis);
        if (Math.Abs(Orbit.InclinationRadians) <= 0.0000001d)
        {
            return planar;
        }

        var cosInclination = Math.Cos(Orbit.InclinationRadians);
        var sinInclination = Math.Sin(Orbit.InclinationRadians);
        return new Vector3d(
            planar.X,
            -planar.Z * sinInclination,
            planar.Z * cosInclination);
    }

    private static double NormalizeAngle(double radians)
    {
        var normalized = radians % Tau;
        return normalized < 0d
            ? normalized + Tau
            : normalized;
    }
}
