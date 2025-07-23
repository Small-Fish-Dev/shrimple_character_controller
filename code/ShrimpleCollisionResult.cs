namespace ShrimpleCharacterController;

public struct ShrimpleCollisionResult
{
    public Vector3 HitPosition;
    public Vector3 HitNormal;
    public Vector3 HitVelocityBefore;
    public Vector3 HitVelocityAfter;
    public float Angle;
    public GameObject HitObject;
    public Surface HitSurface;

    public ShrimpleCollisionResult() { }

    public ShrimpleCollisionResult(Vector3 hitPosition, Vector3 hitNormal, Vector3 hitVelocityBefore, Vector3 hitVelocityAfter, float angle, GameObject hitObject, Surface hitSurface)
    {
        HitPosition = hitPosition;
        HitNormal = hitNormal;
        HitVelocityBefore = hitVelocityBefore;
        HitVelocityAfter = hitVelocityAfter;
        Angle = angle;
        HitObject = hitObject;
        HitSurface = hitSurface;
    }

    public ShrimpleCollisionResult WithHitPosition(Vector3 hitPosition)
    {
        var result = this;
        result.HitPosition = hitPosition;
        return result;
    }

    public ShrimpleCollisionResult WithHitNormal(Vector3 hitNormal)
    {
        var result = this;
        result.HitNormal = hitNormal;
        return result;
    }

    public ShrimpleCollisionResult WithHitVelocityBefore(Vector3 hitVelocityBefore)
    {
        var result = this;
        result.HitVelocityBefore = hitVelocityBefore;
        return result;
    }

    public ShrimpleCollisionResult WithHitVelocityAfter(Vector3 hitVelocityAfter)
    {
        var result = this;
        result.HitVelocityAfter = hitVelocityAfter;
        return result;
    }

    public ShrimpleCollisionResult WithAngle(float angle)
    {
        var result = this;
        result.Angle = angle;
        return result;
    }

    public ShrimpleCollisionResult WithHitObject(GameObject hitObject)
    {
        var result = this;
        result.HitObject = hitObject;
        return result;
    }

    public ShrimpleCollisionResult WithHitSurface(Surface hitSurface)
    {
        var result = this;
        result.HitSurface = hitSurface;
        return result;
    }
}