namespace AIG.Game.Cosmos;

public sealed class Universe
{
    private const double GravitationalConstant = 6.67430e-11d;
    private readonly List<StarSystem> _starSystems;

    public Universe(string name, int seed, IEnumerable<StarSystem> starSystems)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be provided.", nameof(name));
        }

        Name = name;
        Seed = seed;
        _starSystems = starSystems?.ToList() ?? throw new ArgumentNullException(nameof(starSystems));
    }

    public string Name { get; }
    public int Seed { get; }
    public double SimulationTimeSeconds { get; private set; }
    public IReadOnlyList<StarSystem> StarSystems => _starSystems;

    public void AdvanceTime(double deltaSeconds)
    {
        if (deltaSeconds <= 0d)
        {
            return;
        }

        SimulationTimeSeconds += deltaSeconds;
    }

    public bool TryGetDominantBody(Vector3d position, out CelestialBody? body)
    {
        body = null;
        var strongestAcceleration = double.MinValue;
        for (var systemIndex = 0; systemIndex < _starSystems.Count; systemIndex++)
        {
            var system = _starSystems[systemIndex];
            for (var bodyIndex = 0; bodyIndex < system.Bodies.Count; bodyIndex++)
            {
                var candidate = system.Bodies[bodyIndex];
                var acceleration = ComputeGravityAcceleration(candidate, system.GetAbsolutePosition(candidate, SimulationTimeSeconds), position);
                if (acceleration <= strongestAcceleration)
                {
                    continue;
                }

                strongestAcceleration = acceleration;
                body = candidate;
            }
        }

        return body is not null;
    }

    public CelestialBody? GetDominantBody(Vector3d position)
    {
        return TryGetDominantBody(position, out var body)
            ? body
            : null;
    }

    public Vector3d GetGravityAt(Vector3d position)
    {
        var gravity = Vector3d.Zero;
        for (var systemIndex = 0; systemIndex < _starSystems.Count; systemIndex++)
        {
            var system = _starSystems[systemIndex];
            for (var bodyIndex = 0; bodyIndex < system.Bodies.Count; bodyIndex++)
            {
                var body = system.Bodies[bodyIndex];
                var bodyPosition = system.GetAbsolutePosition(body, SimulationTimeSeconds);
                gravity += ComputeGravityVector(body, bodyPosition, position);
            }
        }

        return gravity;
    }

    public static Universe CreateDefault(int seed)
    {
        var star = new CelestialBody(
            name: "Helios",
            kind: CelestialBodyKind.Star,
            mass: 1.98847e30d,
            radius: 696_340_000d,
            rotationPeriodSeconds: 2_192_832d);
        var planet = new CelestialBody(
            name: "AIG-Prime",
            kind: CelestialBodyKind.Planet,
            mass: 5.9722e24d,
            radius: 6_371_000d,
            parent: star,
            orbit: new OrbitParameters(149_597_870_700d, 31_557_600d, initialPhaseRadians: 0.35d),
            rotationPeriodSeconds: 86_164d);
        _ = new CelestialBody(
            name: "AIG-Luna",
            kind: CelestialBodyKind.Moon,
            mass: 7.342e22d,
            radius: 1_737_400d,
            parent: planet,
            orbit: new OrbitParameters(384_400_000d, 2_360_591d, inclinationRadians: 0.089d, initialPhaseRadians: 1.20d),
            rotationPeriodSeconds: 2_360_591d);

        return new Universe(
            name: "AIG Universe",
            seed: seed,
            starSystems:
            [
                new StarSystem("Home System", Vector3d.Zero, star)
            ]);
    }

    private static double ComputeGravityAcceleration(CelestialBody body, Vector3d bodyPosition, Vector3d position)
    {
        var offset = bodyPosition - position;
        var distanceSquared = Math.Max(offset.LengthSquared, body.Radius * body.Radius);
        return GravitationalConstant * body.Mass / distanceSquared;
    }

    private static Vector3d ComputeGravityVector(CelestialBody body, Vector3d bodyPosition, Vector3d position)
    {
        var offset = bodyPosition - position;
        if (offset.LengthSquared <= 0d)
        {
            return Vector3d.Zero;
        }

        var acceleration = ComputeGravityAcceleration(body, bodyPosition, position);
        return offset.Normalize() * acceleration;
    }
}
