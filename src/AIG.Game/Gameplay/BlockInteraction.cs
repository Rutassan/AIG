using System.Numerics;
using AIG.Game.World;

namespace AIG.Game.Gameplay;

public static class BlockInteraction
{
    public static bool TryBreak(WorldMap world, BlockRaycastHit? hit)
    {
        if (hit is null)
        {
            return false;
        }

        var h = hit.Value;
        if (!world.IsInside(h.X, h.Y, h.Z))
        {
            return false;
        }

        if (!world.IsSolid(h.X, h.Y, h.Z))
        {
            return false;
        }

        world.SetBlock(h.X, h.Y, h.Z, BlockType.Air);
        return true;
    }

    public static bool TryPlace(WorldMap world, BlockRaycastHit? hit, BlockType blockType, Func<Vector3, bool> collidesWithPlayer)
    {
        if (hit is null || blockType == BlockType.Air)
        {
            return false;
        }

        var h = hit.Value;
        if (!world.IsInside(h.PreviousX, h.PreviousY, h.PreviousZ))
        {
            return false;
        }

        if (world.IsSolid(h.PreviousX, h.PreviousY, h.PreviousZ))
        {
            return false;
        }

        var placeCenter = new Vector3(h.PreviousX + 0.5f, h.PreviousY + 0.5f, h.PreviousZ + 0.5f);
        if (collidesWithPlayer(placeCenter))
        {
            return false;
        }

        world.SetBlock(h.PreviousX, h.PreviousY, h.PreviousZ, blockType);
        return true;
    }
}
