namespace AIG.Game.Cosmos;

public sealed class StarSystem
{
    private readonly List<CelestialBody> _bodies;

    public StarSystem(string name, Vector3d origin, CelestialBody star)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be provided.", nameof(name));
        }

        if (star.Kind != CelestialBodyKind.Star)
        {
            throw new ArgumentException("Root body must be a star.", nameof(star));
        }

        if (star.Parent is not null)
        {
            throw new ArgumentException("System star must not orbit another body.", nameof(star));
        }

        Name = name;
        Origin = origin;
        Star = star;
        _bodies = FlattenBodies(star);
    }

    public string Name { get; }
    public Vector3d Origin { get; }
    public CelestialBody Star { get; }
    public IReadOnlyList<CelestialBody> Bodies => _bodies;

    public Vector3d GetAbsolutePosition(CelestialBody body, double simulationTimeSeconds)
    {
        if (!_bodies.Contains(body))
        {
            throw new ArgumentException("Body does not belong to this star system.", nameof(body));
        }

        return body.GetAbsolutePosition(simulationTimeSeconds, Origin);
    }

    private static List<CelestialBody> FlattenBodies(CelestialBody root)
    {
        var result = new List<CelestialBody>();
        var stack = new Stack<CelestialBody>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            result.Add(current);
            for (var i = current.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(current.Children[i]);
            }
        }

        return result;
    }
}
