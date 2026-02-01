namespace ShrimpleCharacterController;

public partial class ShrimpleCharacterController
{
    bool _didStep;
    Vector3 _stepPosition;

    void IScenePhysicsEvents.PrePhysicsStep()
    {
        if (!PhysicallySimulated) return;
        if (!Body.IsValid()) return;

        _didStep = false;

        UpdateBodyPhysics();
        UpdateMassCenter();
        AddWishVelocity();

        if (StepsEnabled && IsOnGround)
            TryStepUp();
    }

    void IScenePhysicsEvents.PostPhysicsStep()
    {
        if (!Body.IsValid())
        {
            WorldPosition += GroundVelocity * Time.Delta;
            return;
        }

        if (!PhysicallySimulated) return;

        if (_didStep)
            Body.WorldPosition = _stepPosition;

        var wasOnGround = IsOnGround;

        CategorizePhysicalGround();

        if (GroundStickEnabled && !IsSlipping)
        {
            var shouldTryStick = IsOnGround;
            if (!shouldTryStick && wasOnGround && Body.Velocity.z <= 0f)
                shouldTryStick = true;

            if (shouldTryStick)
                StickToGround();
        }

        ApplyGroundVelocity();
        Velocity = Body.Velocity;
    }

    private void UpdateMassCenter()
    {
        if (!Body.IsValid()) return;

        var wishSpeed = WishVelocity.WithZ(0).Length;
        var halfHeight = AppliedHeight * 0.5f;
        float massCenter = IsOnGround ? wishSpeed.Clamp(0, halfHeight) : halfHeight;

        Body.MassCenterOverride = new Vector3(0, 0, massCenter);
        Body.OverrideMassCenter = true;
    }

    private void UpdateBodyPhysics()
    {
        if (!Collider.IsValid() || !Body.IsValid()) return;

        var onDynamic = false;
        if (IsOnGround && GroundObject.IsValid())
        {
            var groundBody = GroundObject.GetComponent<Rigidbody>();
            onDynamic = groundBody != null && groundBody.PhysicsBody.BodyType == PhysicsBodyType.Dynamic;
        }

        Body.Gravity = false;

        var velocityHorizontal = Body.Velocity.WithZ(0).Length;
        bool wantsBrakes = IsOnGround && WishVelocity.Length < 1f && velocityHorizontal < 10f;
        Body.LinearDamping = wantsBrakes ? 10f : 0.1f;
        Body.AngularDamping = 1f;

        float friction = 0f;
        if (IsOnGround && WishVelocity.Length < 0.1f)
            friction = onDynamic ? 0.5f : 1f;
        Collider.Friction = friction;
    }

    private void AddWishVelocity()
    {
        var wish = WishVelocity;
        var z = Body.Velocity.z;
        var velocity = Body.Velocity;

        var currentSpeed = Math.Max(velocity.WithZ(0).Length, 10f);
        var acceleration = (IsOnGround ? GroundAcceleration : AirAcceleration) * (FixedAcceleration ? 1f : AccelerationCurve.Evaluate(currentSpeed));
        var deceleration = (IsOnGround ? GroundDeceleration : AirDeceleration) * (FixedDeceleration ? 1f : DecelerationCurve.Evaluate(currentSpeed));

        if (!IgnoreGroundSurface && GroundSurface != null)
        {
            acceleration *= GroundSurface.Friction;
            deceleration *= GroundSurface.Friction;
        }

        if (!wish.IsNearZeroLength)
        {
            if (AccelerationEnabled)
            {
                var speed = velocity.WithZ(0).Length;
                var maxSpeed = MathF.Max(wish.Length, speed);

                var targetVelocity = wish;
                if (IsOnGround && GroundStickEnabled && !GroundNormal.IsNearlyZero(0.01f))
                {
                    targetVelocity = Vector3.VectorPlaneProject(wish, GroundNormal);
                    if (!targetVelocity.IsNearlyZero(0.01f))
                    {
                        var isGoingUphill = Vector3.Dot(targetVelocity, AppliedGravity) < 0f;
                        var slopeMultiplier = isGoingUphill ? GetSlopeVelocityMultiplier(GroundAngle) : 1f;
                        targetVelocity = targetVelocity.Normal * maxSpeed * slopeMultiplier;
                    }

                    velocity = velocity.MoveTowards(targetVelocity, acceleration * Time.Delta);

                    if (velocity.Length > maxSpeed)
                        velocity = velocity.Normal * maxSpeed;
                }
                else
                {
                    var horizontalVel = velocity.WithZ(0);
                    var targetHorizontal = wish.Normal * maxSpeed;
                    horizontalVel = horizontalVel.MoveTowards(targetHorizontal, acceleration * Time.Delta);

                    if (horizontalVel.Length > maxSpeed)
                        horizontalVel = horizontalVel.Normal * maxSpeed;

                    velocity = horizontalVel.WithZ(velocity.z);
                }
            }
            else
            {
                if (IsOnGround && GroundStickEnabled && !GroundNormal.IsNearlyZero(0.01f))
                {
                    var projectedWish = Vector3.VectorPlaneProject(wish, GroundNormal);
                    if (!projectedWish.IsNearlyZero(0.01f))
                    {
                        var isGoingUphill = Vector3.Dot(projectedWish, AppliedGravity) < 0f;
                        var slopeMultiplier = isGoingUphill ? GetSlopeVelocityMultiplier(GroundAngle) : 1f;
                        velocity = projectedWish.Normal * wish.Length * slopeMultiplier;
                    }
                    else
                        velocity = Vector3.Zero;
                }
                else
                {
                    if (!IsOnGround)
                        velocity = wish.WithZ(velocity.z);
                    else
                        velocity = wish;
                }
            }
        }
        else if (IsOnGround)
        {
            if (AccelerationEnabled)
                velocity = velocity.MoveTowards(Vector3.Zero, deceleration * Time.Delta);
            else
                velocity = Vector3.Zero;
        }

        if (IsOnGround && !GroundStickEnabled)
            velocity.z = z;

        if (GravityEnabled && (!IsOnGround || IsSlipping || !GroundStickEnabled))
            velocity += AppliedGravity * Time.Delta;

        Body.Velocity = velocity;
    }

    private void TryStepUp()
    {
        var wishHorizontal = WishVelocity.WithZ(0);
        var bodyHorizontal = Body.Velocity.WithZ(0);

        var moveDir = wishHorizontal.IsNearlyZero(0.1f) ? bodyHorizontal : wishHorizontal;
        if (moveDir.IsNearlyZero(0.1f)) return;

        var from = WorldPosition;
        var speed = MathF.Max(bodyHorizontal.Length * Time.Delta, StepDepth);
        var vel = moveDir.Normal * speed;

        var a = from + _offset - vel.Normal * SkinWidth;
        var b = from + _offset + vel;
        var forwardTrace = BuildTrace(_shrunkenBounds, a, b);

        if (forwardTrace.StartedSolid) return;
        if (!forwardTrace.Hit) return;

        var remainingDist = vel.Length - forwardTrace.Distance;
        if (remainingDist <= 0) remainingDist = StepDepth;
        var remainingVel = vel.Normal * remainingDist;

        from = forwardTrace.EndPosition - _offset;
        var upPoint = from + _offset + Vector3.Up * StepHeight;
        var upTrace = BuildTrace(_shrunkenBounds, from + _offset, upPoint);

        if (upTrace.StartedSolid) return;
        if (upTrace.Distance < 2f) return;

        var raisedPos = upTrace.EndPosition;
        var acrossEnd = raisedPos + remainingVel;
        var acrossTrace = BuildTrace(_shrunkenBounds, raisedPos, acrossEnd);

        if (acrossTrace.StartedSolid) return;

        var top = acrossTrace.EndPosition;
        var bottom = top + Vector3.Down * StepHeight;
        var downTrace = BuildTrace(_shrunkenBounds, top, bottom);

        if (!downTrace.Hit) return;

        var groundAngle = Vector3.GetAngle(Vector3.Up, downTrace.Normal);
        if (!IsAngleStandable(groundAngle)) return;

        var newFeetPos = downTrace.EndPosition - _offset + Vector3.Up * SkinWidth;
        if (MathF.Abs(newFeetPos.z - WorldPosition.z) < 0.5f) return;

        _didStep = true;
        _stepPosition = newFeetPos;
        Body.WorldPosition = _stepPosition;
        Body.Velocity = Body.Velocity.WithZ(0);
    }

    private void ApplyGroundVelocity()
    {
        if (!IsOnGround || !GroundStickEnabled || IsSlipping) return;

        var groundVelocity = GroundVelocity;
        if (groundVelocity.IsNearZeroLength) return;

        Body.WorldPosition += groundVelocity * Time.Delta;
    }

    private void StickToGround()
    {
        if (Body.PhysicsBody.Sleeping) return;

        var currentPosition = WorldPosition;
        var from = currentPosition + _offset + Vector3.Up * StepHeight;
        var to = currentPosition + _offset + Vector3.Down * GroundStickDistance;

        var trace = BuildTrace(_shrunkenBounds, from, to);

        if (trace.StartedSolid) return;

        if (trace.Hit)
        {
            var surfaceAngle = Vector3.GetAngle(Vector3.Up, trace.Normal);
            if (!IsAngleStandable(surfaceAngle)) return;

            var targetFeetPos = trace.EndPosition - _offset + Vector3.Up * 0.01f;
            var delta = currentPosition - targetFeetPos;

            if (delta.z < -0.1f) return;
            if (delta.IsNearlyZero(0.001f)) return;

            Body.WorldPosition = targetFeetPos;

            if (delta.z > 0.01f)
                Body.Velocity = Body.Velocity.WithZ(0);

            IsOnGround = true;
            GroundNormal = trace.Normal;
            GroundSurface = trace.Surface;
            GroundObject = trace.GameObject;
            IsSlipping = false;
        }
    }

    private void CategorizePhysicalGround()
    {
        var feetPos = WorldPosition;
        var from = feetPos + Vector3.Up * 2f;
        var to = feetPos + Vector3.Down * 2f;

        var groundTrace = Game.SceneTrace.Sphere(SkinWidth * 2f, from, to)
            .IgnoreGameObjectHierarchy(GameObject)
            .WithoutTags(IgnoreTags)
            .Run();

        if (groundTrace.StartedSolid)
        {
            IsStuck = true;
            var fallbackTrace = BuildTrace(_shrunkenBounds, WorldPosition + _offset + Vector3.Up * 4f, WorldPosition + _offset + Vector3.Down * 2f);
            if (fallbackTrace.Hit && !fallbackTrace.StartedSolid)
            {
                var fallbackAngle = Vector3.GetAngle(Vector3.Up, fallbackTrace.Normal);
                if (IsAngleStandable(fallbackAngle))
                {
                    IsOnGround = true;
                    GroundNormal = fallbackTrace.Normal;
                    GroundSurface = fallbackTrace.Surface;
                    GroundObject = fallbackTrace.GameObject;
                    IsSlipping = false;
                    return;
                }
            }
        }
        else
        {
            IsStuck = false;
        }

        if (groundTrace.Hit && !groundTrace.StartedSolid)
        {
            var surfaceAngle = Vector3.GetAngle(Vector3.Up, groundTrace.Normal);
            var isStandable = IsAngleStandable(surfaceAngle);

            if (isStandable)
            {
                IsOnGround = true;
                GroundNormal = groundTrace.Normal;
                GroundSurface = groundTrace.Surface;
                GroundObject = groundTrace.GameObject;
                IsSlipping = false;
            }
            else
            {
                IsOnGround = false;
                GroundNormal = groundTrace.Normal;
                GroundSurface = groundTrace.Surface;
                GroundObject = groundTrace.GameObject;
                IsSlipping = true;
            }
        }
        else
        {
            IsOnGround = false;
            GroundNormal = Vector3.Up;
            GroundSurface = null;
            GroundObject = null;
            IsSlipping = false;
        }
    }

    public bool TryPhysicalUnstuck(Vector3 position, out Vector3 result)
    {
        if (Body.IsValid())
        {
            var bodyPos = Body.WorldPosition;
            var bodyTrace = BuildTrace(_shrunkenBounds, bodyPos + _offset, bodyPos + _offset);
            if (!bodyTrace.StartedSolid)
            {
                result = bodyPos;
                return true;
            }
        }

        for (int i = 1; i <= 10; i++)
        {
            var upPos = position + -AppliedGravity.Normal * (i * 2f);
            var upTrace = BuildTrace(_shrunkenBounds, upPos + _offset, upPos + _offset);
            if (!upTrace.StartedSolid)
            {
                result = upPos;
                if (Body.IsValid())
                {
                    Body.WorldPosition = upPos;
                    Body.Velocity = Vector3.Zero;
                }
                return true;
            }
        }

        if (Body.IsValid() && Body.Velocity.Length > 1f)
        {
            var velDir = Body.Velocity.Normal;
            for (int i = 1; i <= 5; i++)
            {
                var velPos = position + velDir * (i * 4f);
                var velTrace = BuildTrace(_shrunkenBounds, velPos + _offset, velPos + _offset);
                if (!velTrace.StartedSolid)
                {
                    result = velPos;
                    Body.WorldPosition = velPos;
                    return true;
                }
            }
        }

        result = position;
        return false;
    }

    protected void CreateBody()
    {
        if (Collider.IsValid()) Collider.Destroy();
        if (Body.IsValid()) Body.Destroy();
        if (BodyObject.IsValid()) BodyObject.Destroy();

        CreateCollider();
        CreateRigidbody();
    }

    protected void DestroyBody()
    {
        if (Collider.IsValid()) Collider.Destroy();
        if (Body.IsValid()) Body.Destroy();
        if (BodyObject.IsValid()) BodyObject.Destroy();
    }

    protected void CreateCollider()
    {
        if (Collider.IsValid()) Collider.Destroy();

        if (PhysicallySimulated)
            Collider = GameObject.GetOrAddComponent<CapsuleCollider>();
        else
        {
            if (TraceShape == TraceType.Box || TraceShape == TraceType.Bounds)
                Collider = GameObject.GetOrAddComponent<BoxCollider>();
            else if (TraceShape == TraceType.Cylinder)
                Collider = GameObject.GetOrAddComponent<HullCollider>();
            else if (TraceShape == TraceType.Sphere)
                Collider = GameObject.GetOrAddComponent<SphereCollider>();
        }

        if (HidePhysicalComponents)
            Collider.Flags |= ComponentFlags.Hidden;
        else
            Collider.Flags &= ~ComponentFlags.Hidden;

        UpdateCollider();
    }

    protected void UpdateCollider()
    {
        if (!Collider.IsValid()) return;

        if (Collider is CapsuleCollider capsuleCollider)
        {
            var radius = (TraceWidth - SkinWidth) / 2f;
            var height = TraceHeight - SkinWidth;
            capsuleCollider.Radius = radius;
            capsuleCollider.Start = Vector3.Up * radius;
            capsuleCollider.End = Vector3.Up * (height - radius);
        }
        else if (Collider is BoxCollider boxCollider)
        {
            var bounds = BuildBounds();
            boxCollider.Scale = bounds.Size - SkinWidth;
            boxCollider.Center = Vector3.Up * (TraceHeight / 2f);
        }
        else if (Collider is HullCollider hullCollider)
        {
            hullCollider.Type = HullCollider.PrimitiveType.Cylinder;
            hullCollider.Height = TraceHeight - SkinWidth;
            hullCollider.Radius = (TraceWidth - SkinWidth) / 2f;
            hullCollider.Center = Vector3.Up * (TraceHeight / 2f);
        }
        else if (Collider is SphereCollider sphereCollider)
        {
            sphereCollider.Radius = (TraceWidth - SkinWidth) / 2f;
            sphereCollider.Center = Vector3.Up * sphereCollider.Radius;
        }
    }

    protected void CreateRigidbody()
    {
        if (Body.IsValid()) Body.Destroy();

        if (GameObject.Components.TryGet<Rigidbody>(out var existingBody))
            existingBody.Destroy();

        Body = GameObject.AddComponent<Rigidbody>();
        Body.Locking = new PhysicsLock() { Pitch = true, Roll = true, Yaw = true };
        Body.Gravity = false;
        Body.MassOverride = BodyMassOverride;
        Body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;
        Body.EnhancedCcd = EnableCCD;

        if (HidePhysicalComponents)
            Body.Flags |= ComponentFlags.Hidden;
        else
            Body.Flags &= ~ComponentFlags.Hidden;
    }

    public void OnCollisionStart(Collision collision) { }
}
