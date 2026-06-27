namespace ShrimpleCharacterController;

public partial class ShrimpleCharacterController
{
    bool _didStep;
    Vector3 _stepPosition;

    // Set by Move() when ManuallyUpdate is enabled in physical mode, consumed by the next physics step
    bool _physicalMovePending;
    float _physicalMoveDelta;

    // The delta this physics step should simulate (the manual Move() delta, otherwise the fixed step)
    float ActiveDelta => ( ManuallyUpdate && _physicalMovePending ) ? _physicalMoveDelta : Time.Delta;

    // The solver always integrates over Time.Delta, so we scale velocity to cover ActiveDelta worth of distance
    float MoveScale => Time.Delta > 0f ? ActiveDelta / Time.Delta : 1f;

    void IScenePhysicsEvents.PrePhysicsStep()
    {
        if ( !PhysicallySimulated ) return;
        if ( !Body.IsValid() ) return;
        if ( ManuallyUpdate && !_physicalMovePending ) return; // Wait for a manual Move() call to drive us

        _didStep = false;

        UpdateBodyPhysics();
        UpdateMassCenter();
        UpdateMovement();
    }

    void IScenePhysicsEvents.PostPhysicsStep()
    {
        if ( !Body.IsValid() )
        {
            WorldPosition += GroundVelocity * Time.Delta;
            return;
        }

        if ( !PhysicallySimulated ) return;
        if ( ManuallyUpdate && !_physicalMovePending ) return;

        if ( _didStep )
            Body.WorldPosition = _stepPosition;

        CategorizePhysicalGround();
        ApplyGroundVelocity();
        Velocity = Body.Velocity / MoveScale; // De-scale back to logical units/second

        _physicalMovePending = false;
    }

    private void UpdateMassCenter()
    {
        if ( !Body.IsValid() ) return;

        var wishSpeed = WishVelocity.WithZ( 0 ).Length;
        var halfHeight = AppliedHeight * 0.5f;
        float massCenter = IsOnGround ? wishSpeed.Clamp( 0, halfHeight ) : halfHeight;

        Body.MassCenterOverride = new Vector3( 0, 0, massCenter );
        Body.OverrideMassCenter = true;
    }

    private void UpdateBodyPhysics()
    {
        if ( !Collider.IsValid() || !Body.IsValid() ) return;

        var onDynamic = false;
        if ( IsOnGround && GroundObject.IsValid() )
        {
            var groundBody = GroundObject.GetComponent<Rigidbody>();
            onDynamic = groundBody != null && groundBody.PhysicsBody.BodyType == PhysicsBodyType.Dynamic;
        }

        Body.Gravity = false;

        var velocityHorizontal = Body.Velocity.WithZ( 0 ).Length;
        bool wantsBrakes = IsOnGround && WishVelocity.Length < 1f && velocityHorizontal < 10f;
        Body.LinearDamping = wantsBrakes ? 10f : 0.1f;
        Body.AngularDamping = 1f;

        float friction = 0f;
        if ( IsOnGround && WishVelocity.Length < 0.1f )
            friction = onDynamic ? 0.5f : 1f;
        Collider.Friction = friction;
    }

    private void AddWishVelocity()
    {
        var z = Body.Velocity.z;
        var velocity = CalculateGoalVelocity( ActiveDelta );

        if ( IsOnGround && GroundStickEnabled && !GroundNormal.IsNearlyZero( 0.01f ) )
        {
            if ( MathF.Abs( GroundNormal.z ) > 0.01f )
                velocity = velocity.WithZ( -( GroundNormal.x * velocity.x + GroundNormal.y * velocity.y ) / GroundNormal.z );

            var isGoingUphill = Vector3.Dot( velocity, AppliedGravity ) < 0f;
            var slopeMultiplier = isGoingUphill ? GetSlopeVelocityMultiplier( GroundAngle ) : 1f;
            velocity *= slopeMultiplier;
        }

        if ( IsOnGround && !GroundStickEnabled )
            velocity.z = z;

        if ( GravityEnabled && ( !IsOnGround || IsSlipping || !GroundStickEnabled ) )
            velocity += AppliedGravity * ActiveDelta;

        Body.Velocity = velocity * MoveScale; // Scale so the solver covers ActiveDelta worth of distance over Time.Delta
    }

    private void UpdateMovement()
    {
        if ( IsOnGround && GroundStickEnabled && !IsSlipping )
            StickToGround();

        AddWishVelocity();

        var wishHorizontal = WishVelocity.WithZ( 0 );
        var bodyHorizontal = Body.Velocity.WithZ( 0 );
        var moveDir = wishHorizontal.IsNearlyZero( 0.1f ) ? bodyHorizontal : wishHorizontal;
        if ( moveDir.IsNearlyZero( 0.1f ) ) return;

        var speed = MathF.Max( bodyHorizontal.Length * Time.Delta, StepDepth );
        var vel = moveDir.Normal * speed;
        var forwardTrace = BuildTrace( _shrunkenBounds, WorldPosition + _offset - vel.Normal * SkinWidth, WorldPosition + _offset + vel );

        if ( !forwardTrace.Hit || forwardTrace.StartedSolid ) return;

        var forwardAngle = Vector3.GetAngle( -AppliedGravity.Normal, forwardTrace.Normal );
        if ( IsAngleStandable( forwardAngle ) && Vector3.Dot( Body.Velocity, forwardTrace.Normal ) < 0f )
            Body.Velocity = Vector3.VectorPlaneProject( Body.Velocity, forwardTrace.Normal );

        if ( StepsEnabled && IsOnGround )
            TryStepUp( forwardTrace, vel );
    }

    private void TryStepUp( SceneTraceResult forwardTrace, Vector3 vel )
    {
        if ( forwardTrace.StartedSolid || !forwardTrace.Hit ) return;

        var angle = Vector3.GetAngle( -GravityNormal, forwardTrace.Normal );
        if ( angle < 90f - StepTolerance || angle > 90f + StepTolerance ) return;

        var stepHorizontal = Vector3.VectorPlaneProject( vel.Normal, GravityNormal ).Normal * StepDepth;
        var stepVertical = -GravityNormal * ( StepHeight + SkinWidth );
        var stepTrace = BuildTrace( _shrunkenBounds, forwardTrace.EndPosition + stepHorizontal + stepVertical, forwardTrace.EndPosition + stepHorizontal );

        if ( stepTrace.StartedSolid || !stepTrace.Hit ) return;
        if ( !IsAngleStandable( Vector3.GetAngle( stepTrace.Normal, -GravityNormal ) ) ) return;

        var newFeetPos = stepTrace.EndPosition - _offset + _upAxis * SkinWidth;
        if ( newFeetPos.z - WorldPosition.z < 0.5f ) return;

        _didStep = true;
        _stepPosition = newFeetPos;
        Body.WorldPosition = _stepPosition;
        Body.Velocity = Body.Velocity.WithZ( 0 );
    }

    private void ApplyGroundVelocity()
    {
        if ( !IsOnGround || !GroundStickEnabled || IsSlipping ) return;

        var groundVelocity = PlatformVelocity * Time.Delta + SurfaceVelocity * ActiveDelta;
        if ( groundVelocity.IsNearZeroLength ) return;

        Body.WorldPosition += groundVelocity;
    }

    private void StickToGround()
    {
        var currentPosition = WorldPosition;
        var from = currentPosition + _offset - GravityNormal * StepHeight;
        var to = currentPosition + _offset + GravityNormal * ( GroundStickDistance + SkinWidth );

        var trace = BuildTrace( _shrunkenBounds, from, to );

        if ( trace.StartedSolid ) return;

        if ( trace.Hit )
        {
            var surfaceAngle = Vector3.GetAngle( -GravityNormal, trace.Normal );
            if ( !IsAngleStandable( surfaceAngle ) ) return;

            var targetFeetPos = trace.EndPosition - _offset + _upAxis * SkinWidth;

            Body.WorldPosition = targetFeetPos;
            Body.Sleeping = false;

            if ( Body.Velocity.z > 0f )
                Body.Velocity = Body.Velocity.WithZ( 0 );

            IsOnGround = true;
            GroundNormal = trace.Normal;
            GroundSurface = trace.Surface;
            GroundObject = trace.GameObject;
            IsSlipping = false;
        }
    }

    private void CategorizePhysicalGround()
    {
        var position = WorldPosition + _offset;
        var shortCylinderOffset = AppliedWidth / 2f;
        var shortCylinderBounds = new BBox( _shrunkenBounds.Mins, new Vector3( _shrunkenBounds.Maxs.x, _shrunkenBounds.Maxs.y, _shrunkenBounds.Maxs.z - shortCylinderOffset ) );
        var groundTrace = BuildTrace( shortCylinderBounds, position + GravityNormal * shortCylinderOffset / 2f, position + GravityNormal * ( GroundStickDistance + SkinWidth ) );

        if ( groundTrace.StartedSolid )
        {
            var stuckCheck = BuildTrace( _shrunkenBounds.Grow( -SkinWidth ), position, position, useCapsule: true );

            if ( stuckCheck.StartedSolid )
            {
                IsStuck = true;

                if ( UnstuckEnabled && TryUnstuck( position, out var unstuckResult ) )
                {
                    IsStuck = false;
                    Body.WorldPosition = unstuckResult - _offset;
                    Body.Velocity = Vector3.Zero;
                }

                var fallbackTrace = BuildTrace( _shrunkenBounds, position + -GravityNormal * 4f, position + GravityNormal * 2f );

                if ( fallbackTrace.Hit && !fallbackTrace.StartedSolid && Vector3.Dot( fallbackTrace.Normal, -GravityNormal ) > 0f )
                {
                    var standable = IsAngleStandable( Vector3.GetAngle( Vector3.Up, fallbackTrace.Normal ) );
                    IsOnGround = true;
                    GroundNormal = fallbackTrace.Normal;
                    GroundSurface = fallbackTrace.Surface;
                    GroundObject = fallbackTrace.GameObject;
                    IsSlipping = !standable;
                }
                return;
            }

            groundTrace = BuildTrace( _shrunkenBounds, position, position + GravityNormal * ( GroundStickDistance + SkinWidth ) );
        }

        IsStuck = false;

        if ( groundTrace.Hit )
        {
            var standable = IsAngleStandable( Vector3.GetAngle( Vector3.Up, groundTrace.Normal ) );
            var landingAngle = Vector3.Dot( Velocity.Normal, groundTrace.Normal );
            var verticalAngle = Vector3.Dot( Velocity.Normal, GravityNormal );
            var goingUp = verticalAngle < 0.1f;
            var hasLanded = !IsOnGround && landingAngle <= ( MaxGroundAngle.Max / 180f ) && groundTrace.Distance <= SkinWidth * ( goingUp ? 6f : 2f ) + StepHeight;

            IsOnGround = IsOnGround || hasLanded;
            GroundNormal = groundTrace.Normal;
            GroundSurface = groundTrace.Surface;
            GroundObject = groundTrace.GameObject;
            IsSlipping = IsOnGround && !standable;

            if ( GroundStickEnabled && !IsSlipping && IsOnGround )
            {
                var targetFeetPos = groundTrace.EndPosition - _offset + _upAxis * ( SkinWidth + shortCylinderOffset / 2f );
                var delta = WorldPosition - targetFeetPos;

                if ( delta.z >= -0.1f && !delta.IsNearlyZero( 0.001f ) )
                {
                    Body.WorldPosition = targetFeetPos;
                    Body.Sleeping = false;

                    if ( Body.Velocity.z > 0f )
                        Body.Velocity = Body.Velocity.WithZ( 0 );
                }
            }

            return;
        }

        IsOnGround = false;
        GroundNormal = Vector3.Zero;
        GroundSurface = null;
        GroundObject = null;
        IsSlipping = false;
    }

    protected void CreateBody( bool recreate = false )
    {
        if ( recreate )
        {
            if ( Collider.IsValid() ) Collider.Destroy();
            if ( Body.IsValid() ) Body.Destroy();
            if ( BodyObject.IsValid() ) BodyObject.Destroy();
        }

        CreateCollider( recreate );
        CreateRigidbody( recreate );
    }

    protected void DestroyBody()
    {
        if ( Collider.IsValid() ) Collider.Destroy();
        if ( Body.IsValid() ) Body.Destroy();
        if ( BodyObject.IsValid() ) BodyObject.Destroy();
    }

    protected void CreateCollider( bool recreate = false )
    {
        if ( recreate && Collider.IsValid() )
            Collider.Destroy();

        if ( PhysicallySimulated )
        {
            if ( !Collider.IsValid() || Collider is not CapsuleCollider )
            {
                if ( Collider.IsValid() )
                    Collider.Destroy();

                Collider = GameObject.GetOrAddComponent<CapsuleCollider>();
            }
        }
        else
        {
            if ( TraceShape == TraceType.Box || TraceShape == TraceType.Bounds )
            {
                if ( !Collider.IsValid() || Collider is not BoxCollider )
                {
                    if ( Collider.IsValid() )
                        Collider.Destroy();

                    Collider = GameObject.GetOrAddComponent<BoxCollider>();
                }
            }
            else if ( TraceShape == TraceType.Cylinder )
            {
                if ( !Collider.IsValid() || Collider is not HullCollider )
                {
                    if ( Collider.IsValid() )
                        Collider.Destroy();

                    Collider = GameObject.GetOrAddComponent<HullCollider>();
                }
            }
            else if ( TraceShape == TraceType.Sphere )
            {
                if ( !Collider.IsValid() || Collider is not SphereCollider )
                {
                    if ( Collider.IsValid() )
                        Collider.Destroy();

                    Collider = GameObject.GetOrAddComponent<SphereCollider>();
                }
            }
        }

        if ( HidePhysicalComponents )
            Collider.Flags |= ComponentFlags.Hidden;
        else
            Collider.Flags &= ~ComponentFlags.Hidden;

        UpdateCollider();
    }

    protected void UpdateCollider()
    {
        if ( !Collider.IsValid() ) return;

        if ( Collider is CapsuleCollider capsuleCollider )
        {
            var radius = ( TraceWidth - SkinWidth ) / 2f;
            var height = TraceHeight - SkinWidth;
            capsuleCollider.Radius = radius;
            capsuleCollider.Start = Vector3.Up * radius;
            capsuleCollider.End = Vector3.Up * ( height - radius );
        }
        else if ( Collider is BoxCollider boxCollider )
        {
            var bounds = BuildBounds();
            boxCollider.Scale = bounds.Size - SkinWidth;
            boxCollider.Center = Vector3.Up * ( TraceHeight / 2f );
        }
        else if ( Collider is HullCollider hullCollider )
        {
            hullCollider.Type = HullCollider.PrimitiveType.Cylinder;
            hullCollider.Height = TraceHeight - SkinWidth;
            hullCollider.Radius = ( TraceWidth - SkinWidth ) / 2f;
            hullCollider.Center = Vector3.Up * ( TraceHeight / 2f );
        }
        else if ( Collider is SphereCollider sphereCollider )
        {
            sphereCollider.Radius = ( TraceWidth - SkinWidth ) / 2f;
            sphereCollider.Center = Vector3.Up * sphereCollider.Radius;
        }
    }

    protected void CreateRigidbody( bool recreate = false )
    {
        if ( recreate && Body.IsValid() )
            Body.Destroy();

        if ( !Body.IsValid() && GameObject.Components.TryGet<Rigidbody>( out var existingBody ) )
            Body = existingBody;
        if ( !Body.IsValid() )
            Body = GameObject.AddComponent<Rigidbody>();

        Body.Locking = new PhysicsLock() { Pitch = true, Roll = true, Yaw = true };
        Body.Gravity = false;
        Body.MassOverride = BodyMassOverride;
        Body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;
        Body.EnhancedCcd = EnableCCD;

        if ( HidePhysicalComponents )
            Body.Flags |= ComponentFlags.Hidden;
        else
            Body.Flags &= ~ComponentFlags.Hidden;
    }

    public void OnCollisionStart( Collision collision ) { }
}
