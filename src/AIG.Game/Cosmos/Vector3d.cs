namespace AIG.Game.Cosmos;

public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d Zero => new(0d, 0d, 0d);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public static Vector3d operator +(Vector3d left, Vector3d right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    public static Vector3d operator -(Vector3d left, Vector3d right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    public static Vector3d operator *(Vector3d value, double scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
    public static Vector3d operator *(double scalar, Vector3d value) => value * scalar;
    public static Vector3d operator /(Vector3d value, double scalar) => scalar == 0d ? Zero : new(value.X / scalar, value.Y / scalar, value.Z / scalar);

    public Vector3d Normalize()
    {
        var length = Length;
        return length <= 0d
            ? Zero
            : this / length;
    }
}
