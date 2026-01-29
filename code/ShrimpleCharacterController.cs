using System.Text.Json.Nodes;

namespace ShrimpleCharacterController;

[Icon("nordic_walking")]
public class ShrimpleCharacterController : Component, IScenePhysicsEvents, ISceneEvent<IScenePhysicsEvents>, Component.ICollisionListener
{
    /// <summary>
    /// Manually update this by calling Move() or let it always be simulated
    /// </summary>
    [Property]
    [Group("Options")]
    [Validate(nameof(physicalAndManual), "When manually updating a simulated body make sure to call Move() before the physics step!", LogLevel.Warn)]
    public bool ManuallyUpdate { get; set; } = false;

    [Property]
    [FeatureEnabled("Physical")]
    [Validate(nameof(physicalAndManual), "When manually updating a simulated body make sure to call Move() before the physics step!", LogLevel.Warn)]
    public bool PhysicallySimulated
    {
        get;
        set
        {
            field = value;

            if (value)
                CreateBody();
            else
                DestroyBody();
        }
    }

    private bool physicalAndManual(object _) => !ManuallyUpdate || !PhysicallySimulated;
    private bool isPhysical(object _) => !PhysicallySimulated;

    [Property]
    [Hide]
    public GameObject BodyObject { get; protected set; }

    [Property]
    [Feature("Physical")]
    [Header("Collider")]
    [Validate(nameof(isPhysical), "Physical mode enabled - controller can interact with and be pushed by physics objects", LogLevel.Info)]
    public Surface ColliderSurface
    {
        get => Collider.IsValid() ? Collider.Surface : null;
        set
        {
            if (Collider.IsValid())
                Collider.Surface = value;
        }
    }

    [Property]
    [Feature("Physical")]
    public float? ColliderElasticity
    {
        get => Collider.IsValid() ? Collider.Elasticity : null;
        set
        {
            if (Collider.IsValid())
                Collider.Elasticity = value;
        }
    }

    [Property]
    [Feature("Physical")]
    [ShowIf("HidePhysicalComponents", false)]
    public Collider Collider { get; protected set; }

    /// <summary>
    /// Kilos, tweak based on your surroundings, default boxes are like 600kg!
    /// </summary>
    [Property]
    [Feature("Physical")]
    [Header("Rigid Body")]
    public float BodyMassOverride
    {
        get;
        set
        {
            field = value;

            if (Body.IsValid())
                Body.MassOverride = value;
        }
    } = 100f;

    [Property]
    [Feature("Physical")]
    public RigidbodyFlags BodyRigidbodyFlags
    {
        get => Body.IsValid() ? Body.RigidbodyFlags : 0;
        set
        {
            if (Body.IsValid())
                Body.RigidbodyFlags = value;
        }
    }

    /// <summary>
    /// Enable Continuous Collision Detection (CCD) for the physics body.
    /// Useful for fast-moving characters to prevent tunneling through thin objects.
    /// </summary>
    [Property]
    [Feature("Physical")]
    [Title("Enable CCD")]
    public bool EnableCCD
    {
        get;
        set
        {
            field = value;
            if (Body.IsValid())
                Body.EnhancedCcd = value;
        }
    }

    [Property]
    [Feature("Physical")]
    [ShowIf("HidePhysicalComponents", false)]
    public Rigidbody Body { get; protected set; }

    /// <summary>
    /// Hide the <see cref="Body"/> and <see cref="Collider"/> components in the inspector
    /// </summary>
    [Property]
    [Feature("Physical")]
    [Title("Hide Components")]
    [Space]
    [Validate(nameof(isPhysical), "Make sure to go over the other features to see any warning regarding physical simulation!", LogLevel.Warn)]
    public bool HidePhysicalComponents
    {
        get;
        protected set
        {
            field = value;

            if (value)
            {
                if (Body.IsValid())
                    Body.Flags |= ComponentFlags.Hidden;
                if (Collider.IsValid())
                    Collider.Flags |= ComponentFlags.Hidden;
            }
            else
            {
                if (Body.IsValid())
                    Body.Flags &= ~ComponentFlags.Hidden;
                if (Collider.IsValid())
                    Collider.Flags &= ~ComponentFlags.Hidden;
            }
        }
    } = true;

    [Feature("Physical")]
    [Button("Recreate Components", "sync")]
    public void RefreshBody() => CreateBody();

    /// <summary>
    /// If pushing against a wall, scale the velocity based on the wall's angle (False is useful for NPCs that get stuck on corners)
    /// </summary>
    [Property]
    [Group("Options")]
    public bool ScaleAgainstWalls { get; set; } = true;

    /// <summary>
    /// Rotate the trace with the gameobject
    /// </summary>
    [Property]
    [Group("Trace")]
    [Validate(nameof(isPhysical), "Physical mode uses a capsule collider to prevent snagging on terrain.", LogLevel.Info)]
    public bool RotateWithGameObject { get; set; } = true;

    public enum TraceType
    {
        /// <summary>
        /// This has no drawback, enjoy!
        /// </summary>
        [Icon("📦")]
        Box,
        /// <summary>
        /// This is a PHYSICAL TRACE, so it's more expensive than the normal box or sphere trace
        /// </summary>
        [Icon("🛢")]
        Cylinder,
        /// <summary>
        /// This will disable <see cref="StepsEnabled"/> as it's impossible to get the angle reliably
        /// </summary>
        [Icon("🏐")]
        Sphere,
        /// <summary>
        /// No drawbacks, box but you can define exact bounds
        /// </summary>
        [Icon("🧊")]
        Bounds
    }

    [Property]
    [Group("Trace")]
    [HideIf(nameof(PhysicallySimulated), true)]
    public TraceType TraceShape
    {
        get;
        set
        {
            field = value;
            RebuildBounds();
            CreateCollider();
        }
    } = TraceType.Box;

    /// <summary>
    /// Width of our trace
    /// </summary>
    [Property]
    [Group("Trace")]
    [ShowIf(nameof(_showWidthHeight), true)]
    [Range(1f, 128f, false, true)]
    [Sync]
    public float TraceWidth
    {
        get;
        set
        {
            field = value;
            RebuildBounds();
            UpdateCollider();
        }
    } = 16f;

    // Show width/height if Physical mode OR if not using Bounds shape
    bool _showWidthHeight => PhysicallySimulated || TraceShape != TraceType.Bounds;
    // Show height if Physical mode OR if using Box/Cylinder shape
    bool _showHeight => PhysicallySimulated || TraceShape == TraceType.Box || TraceShape == TraceType.Cylinder;

    /// <summary>
    /// Height of our trace
    /// </summary>
    [Property]
    [Group("Trace")]
    [ShowIf(nameof(_showHeight), true)]
    [Range(1f, 256f, false, true)]
    [Sync]
    public float TraceHeight
    {
        get;
        set
        {
            field = value;
            RebuildBounds();
            UpdateCollider();
        }
    } = 72f;

    /// <summary>
    /// Bounds of our trace
    /// </summary>
    [Property]
    [Group("Trace")]
    [ShowIf("TraceShape", TraceType.Bounds)]
    [Sync]
    public BBox TraceBounds
    {
        get => TraceShape == TraceType.Bounds ? field : Bounds;
        set
        {
            field = value;
            RebuildBounds();
            UpdateCollider();
        }
    } = BBox.FromPositionAndSize(Vector3.Up * 36f, new Vector3(32f, 32f, 72f));

    private bool ignoreTagsAndPhysical(TagSet tagSet) => tagSet.IsEmpty || !PhysicallySimulated;

    /// <summary>
    /// Which tags it should ignore
    /// </summary>
    [Property]
    [Group("Trace")]
    [Validate(nameof(ignoreTagsAndPhysical), "Contoller is physical! Ignore tags must be set on the collision matrix", LogLevel.Warn)]
    public TagSet IgnoreTags { get; set; } = new TagSet();

    /// <summary>
    /// Max amount of trace calls whenever the simulation doesn't reach its target (Slide and collide bounces)
    /// </summary>
    [Property]
    [Group("Trace")]
    [Range(1, 20, true, true)]
    public int MaxBounces { get; set; } = 5;

    /// <summary>
    /// Acceleration and Deceleration
    /// </summary>
    [FeatureEnabled("Acceleration")]
    [Property]
    public bool AccelerationEnabled { get; set; } = true;

    /// <summary>
    /// How fast you accelerate while on the ground (Units per second)
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    [Range(0f, 3000f, false)]
    public float GroundAcceleration { get; set; } = 1000f;

    /// <summary>
    /// How fast you accelerate while in the air (Units per second)
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    [Range(0f, 3000f, false)]
    public float AirAcceleration { get; set; } = 300f;

    /// <summary>
    /// Use the fixed acceleration value instead of curves
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    public bool FixedAcceleration { get; set; } = true;

    protected static readonly Curve DefaultAcceleration = new Curve(new List<Curve.Frame>
    {
        new Curve.Frame(0f, 0.2f, 0f, 1.5f),
        new Curve.Frame(1f, 1f, 0f, 0f)
    })
    {
        TimeRange = new Vector2(0f, 500f),
        ValueRange = new Vector2(0f, 1f),
    };

    /// <summary>
    /// How much acceleration based on the current velocity<br/>
    /// X axis = Current Velocity (Maxes at 500 by default but you can modify)<br/>
    /// Y axis *= Acceleration/><br/>
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    [ValueRange(0f, 1f, false)]
    [HideIf("FixedAcceleration", true)]
    public Curve AccelerationCurve { get; set; } = DefaultAcceleration;

    /// <summary>
    /// How fast you decelerate while on the ground (Units per second)
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    [Range(0f, 3000f, false)]
    public float GroundDeceleration { get; set; } = 1500f;

    /// <summary>
    /// How fast you decelerate while in the air (Units per second)
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    [Range(0f, 3000f, false)]
    public float AirDeceleration { get; set; } = 0f;

    /// <summary>
    /// Use the fixed deceleration value instead of curves
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    public bool FixedDeceleration { get; set; } = true;

    protected static readonly Curve DefaultDeceleration = new Curve(new List<Curve.Frame>
    {
        new Curve.Frame(0f, 1f, 0f, -2.5f),
        new Curve.Frame(1f, 0.2f, 0f, 0f)
    })
    {
        TimeRange = new Vector2(0f, 500f),
        ValueRange = new Vector2(0f, 1f),
    };

    /// <summary>
    /// How much deceleration based on the current velocity<br/>
    /// X axis = Current Velocity (Maxes at 500 by default but you can modify)<br/>
    /// Y axis *= Deceleration/><br/>
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    [ValueRange(0f, 1f, false)]
    [HideIf("FixedDeceleration", true)]
    public Curve DecelerationCurve { get; set; } = DefaultDeceleration;

    /// <summary>
    /// Do we ignore the friction of the surface you're standing on or not?
    /// </summary>
    [Property]
    [Feature("Acceleration")]
    public bool IgnoreGroundSurface { get; set; } = false;

    /// <summary>
    /// Is this MoveHelper meant for horizontal grounded movement? (false = For flying or noclip)
    /// </summary>
    [Property]
    [Group("Movement")]
    public bool IgnoreZ { get; set; } = true;

    /// <summary>
    /// Do we ignore Z when it's near 0 (So that gravity affects you when not moving)
    /// </summary>
    [Property]
    [Title("Ignore Z When Zero")]
    [Group("Movement")]
    [HideIf("IgnoreZ", true)]
    public bool IgnoreZWhenZero { get; set; } = true;

    /// <summary>
    /// Tolerance from a 90° surface before it's considered a wall (Ex. Tolerance 1 = Between 89° and 91° can be a wall, 0.1 = 89.9° to 90.1°)
    /// </summary>
    [Group("Movement")]
    [Property]
    [Range(0f, 10f, false)]
    public float WallTolerance { get; set; } = 1f;

    /// <summary>
    /// Player feels like it's gripping walls too much? Try more Grip Factor Reduction!
    /// </summary>
    [Group("Movement")]
    [Property]
    [Range(1f, 10f, true)]
    [Validate(nameof(isPhysical), "Physical mode uses collider friction for wall interaction", LogLevel.Info)]
    public float GripFactorReduction { get; set; } = 1f;

    /// <summary>
    /// Bouncing off walls and ground on collision
    /// </summary>
    [FeatureEnabled("Bouncing")]
    [Property]
    public bool BouncingEnabled { get; set; } = false;

    /// <summary>
    /// How much the MoveHelper will "bounce" off walls when colliding with them<br/>
    /// (0f = No bounce, 1f = Full bounce)<br/>
    /// </summary>
    [Property]
    [Feature("Bouncing")]
    [Range(0f, 1f, true)]
    [Validate(nameof(isPhysical), "Contoller is physical! Elasticity must be done through the collider", LogLevel.Error)]
    public float HorizontalElasticity { get; set; } = 0f;

    /// <summary>
    /// How much the MoveHelper will "bounce" off the ground when colliding with it<br/>
    /// (0f = No bounce, 1f = Full bounce)<br/>
    /// <see cref="GroundStickEnabled"/> will override this value when sticking to the ground<br/>
    /// </summary>
    [Property]
    [Feature("Bouncing")]
    [Range(0f, 1f, true)]
    public float VerticalElasticity { get; set; } = 0f;

    /// <summary>
    /// Include the ground elasticity when bouncing off the ground<br/>
    /// Make sure the ground surface doesn't have a very low elasticity!
    /// </summary>
    [Property]
    [Feature("Bouncing")]
    public bool IncludeGroundElasticity { get; set; } = false;

    /// <summary>
    /// Minimum velocity to allow bouncing
    /// </summary>
    [Property]
    [Feature("Bouncing")]
    [Range(0f, 100f, true)]
    public float ElasticityThreshold { get; set; } = 30f;

    /// <summary>
    /// Stick the MoveHelper to the ground (IsOnGround will default to false if disabled)
    /// </summary>
    [FeatureEnabled("GroundStick")]
    [Property]
    public bool GroundStickEnabled { get; set; } = true;

    /// <summary>
    /// How steep terrain can be for you to stand on without slipping.<br/>
    /// Min = Angle where velocity starts to drop off<br/>
    /// Max = Max angle where velocity is fully blocked (you can still slide down with gravity)
    /// </summary>
    [Property]
    [Feature("GroundStick")]
    public RangedFloat MaxGroundAngle
    {
        get;
        set
        {
            // Clamp values to valid range and ensure x <= y
            var min = MathX.Clamp(value.Min, 0f, 89f);
            var max = MathX.Clamp(value.Max, 0f, 89f);
            if (min > max) min = max;
            if (max < min) max = min;

            field = new RangedFloat(min, max);
        }
    } = new RangedFloat(30f, 60f);

    /// <summary>
    /// How far from the ground the MoveHelper is going to stick (Useful for going down stairs!)
    /// </summary>
    [Property]
    [Feature("GroundStick")]
    [Range(1f, 32f, false)]
    public float GroundStickDistance { get; set; } = 12f;

    /// <summary>
    /// Stick to any <see cref="GroundObject"/> when standing on it
    /// </summary>
    [Property]
    [Feature("GroundStick")]
    public bool StickToPlatforms { get; set; } = true;

    /// <summary>
    /// When the surface you're on has a surface velocity, apply it to the controller
    /// </summary>
    [Property]
    [Feature("GroundStick")]
    public bool ApplySurfaceVelocity { get; set; } = true;

    /// <summary>
    /// Enable steps climbing (+1 Trace call)
    /// </summary>
    [FeatureEnabled("Steps")]
    [Property]
    public bool StepsEnabled { get; set; } = true;

    /// <summary>
    /// How high steps can be for you to climb on
    /// </summary>
    [Feature("Steps")]
    [Property]
    [Range(1f, 32f, false)]
    public float StepHeight { get; set; } = 12f;

    /// <summary>
    /// How deep it checks for steps (Minimum depth)
    /// </summary>
    [Feature("Steps")]
    [Property]
    [Range(0.1f, 8f, false)]
    public float StepDepth { get; set; } = 2f;

    /// <summary>
    /// Tolerance from a 90° surface before it's considered a valid step (Ex. Tolerance 1 = Between 89° and 91° can be a step, 0.1 = 89.9° to 90.1°)
    /// </summary>
    [Feature("Steps")]
    [Property]
    [Range(0f, 10f, false)]
    public float StepTolerance { get; set; } = 1f;

    /// <summary>
    /// Enable to ability to walk on a surface that's too steep if it's equal or smaller than a step (+1 Trace call when on steep terrain)
    /// </summary>
    [Feature("Steps")]
    [Property]
    public bool PseudoStepsEnabled { get; set; } = true;

    /// <summary>
    /// Instead of colliding with these tags the MoveHelper will be pushed away (Make sure the tags are in IgnoreTags as well!)
    /// </summary>
    [FeatureEnabled("Push")]
    [Property]
    public bool PushEnabled { get; set; } = false;

    /// <summary>
    /// Which tags will push this MoveHelper away and with how much force (Make sure they are also included in IgnoreTags!) (+1 Trace call)
    /// </summary>
    [Property]
    [Feature("Push")]
    [Validate(nameof(isPhysical), "Contoller is physical! Make sure the tags are ignored on the collision matrix", LogLevel.Warn)]
    [Sync]
    public Dictionary<string, float> PushTagsWeight
    {
        get;
        set
        {
            field = value;
            _pushTags = BuildPushTags();
        }
    } = new Dictionary<string, float>() { { "player", 1f } };

    /// <summary>
    /// Apply gravity to this MoveHelper when not on the ground
    /// </summary>
    [FeatureEnabled("Gravity")]
    [Property]
    public bool GravityEnabled { get; set; } = true;

    /// <summary>
    /// Use the scene's gravity or our own
    /// </summary>
    [Property]
    [Feature("Gravity")]
    public bool UseSceneGravity
    {
        get;
        set
        {
            field = value;
            _appliedGravity = BuildGravity();
        }
    } = true;

    /// <summary>
    /// Use a Vector3 gravity instead of a single float (Use this if you want to use a custom gravity)
    /// </summary>
    [Property]
    [Feature("Gravity")]
    [HideIf("UseSceneGravity", true)]
    public bool UseVectorGravity
    {
        get;
        set
        {
            field = value;
            _appliedGravity = BuildGravity();
        }
    }

    private bool _usingFloatGravity => !UseVectorGravity && !UseSceneGravity;
    private bool _usingVectorGravity => UseVectorGravity && !UseSceneGravity;

    /// <summary>
    /// Units per second squared (Default is -850f)
    /// </summary>
    [Property]
    [Feature("Gravity")]
    [Range(-2000, 2000, false)]
    [ShowIf("_usingFloatGravity", true)]
    public float Gravity
    {
        get;
        set
        {
            field = value;
            _appliedGravity = BuildGravity();
        }
    } = -850f;

    /// <summary>
    /// Units per second squared (Default is 0f, 0f, -850f)<br/>
    /// Changes which way <see cref="GroundStickEnabled"/> sticks to the ground
    /// </summary>
    [Property]
    [Feature("Gravity")]
    [ShowIf("_usingVectorGravity", true)]
    public Vector3 VectorGravity
    {
        get;
        set
        {
            field = value;
            _appliedGravity = BuildGravity();
        }
    } = new Vector3(0f, 0f, -850f);

    private Vector3 _appliedGravity;
    public Vector3 AppliedGravity => _appliedGravity;

    /// <summary>
    /// Check if the MoveHelper is stuck and try to get it to unstuck (+Trace calls if stuck)
    /// </summary>
    [FeatureEnabled("Unstuck")]
    [Property]
    public bool UnstuckEnabled { get; set; } = true;

    /// <summary>
    /// How many trace calls it will attempt to get the MoveHelper unstuck
    /// </summary>
    [Property]
    [Feature("Unstuck")]
    [Range(1, 100, false)]
    [Step(1f)]
    [Validate(nameof(isPhysical), "Physical mode uses physics-aware unstuck. Check IsStuck for stuck state.", LogLevel.Info)]
    public int MaxUnstuckTries { get; set; } = 40;

    /// <summary>
    /// The simulated target velocity for our MoveHelper (Units per second, we apply Time.Delta inside)
    /// </summary>
    [Sync] public Vector3 WishVelocity { get; set; }

    /// <summary>
    /// The resulting velocity after the simulation is done (Units per second)
    /// </summary>
    [Sync] public Vector3 Velocity { get; set; }

    /// <summary>
    /// Velocity controlled by outside factors, such as knockback, rootmotion, etc.
    /// It is only applied to our final position and doesn't affect our Velocity.
    /// It should be handled outside the controller.
    /// </summary>
    [Sync] public Vector3 ExternalVelocity { get; set; }

    /// <summary>
    /// Is the MoveHelper currently touching the ground
    /// </summary>
    [Sync] public bool IsOnGround { get; set; }

    /// <summary>
    /// The current ground normal you're standing on (Always Vector3.Zero if IsOnGround false)
    /// </summary>
    public Vector3 GroundNormal { get; private set; } = Vector3.Zero;

    /// <summary>
    /// The current ground angle you're standing on (Always 0f if IsOnGround false)
    /// </summary>
    public float GroundAngle => Vector3.GetAngle(GroundNormal, Vector3.Up);

    /// <summary>
    /// Gets the velocity multiplier for a given slope angle based on MaxGroundAngle range.<br/>
    /// Returns 1.0 below the start angle, 0.0 at/above the max angle, and lerps between.
    /// </summary>
    public float GetSlopeVelocityMultiplier(float angle)
    {
        if (angle <= MaxGroundAngle.Min) return 1f;
        if (angle >= MaxGroundAngle.Max) return 0f;
        return 1f - MathX.LerpInverse(angle, MaxGroundAngle.Min, MaxGroundAngle.Max);
    }

    /// <summary>
    /// Whether the given angle is considered standable (below max ground angle)
    /// </summary>
    public bool IsAngleStandable(float angle) => angle <= MaxGroundAngle.Max;

    /// <summary>
    /// The current surface you're standing on
    /// </summary>
    public Surface GroundSurface { get; private set; }

    /// <summary>
    /// The gameobject you're currently standing on
    /// </summary>
    public GameObject GroundObject { get; set; }

    /// <summary>
    /// Is the MoveHelper currently pushing against a wall
    /// </summary>
    public bool IsPushingAgainstWall { get; private set; }

    /// <summary>
    /// The current wall normal you're pushing against (Always Vector3.Zero if IsPushingAgainstWall false)
    /// </summary>
    public Vector3 WallNormal { get; private set; } = Vector3.Zero;

    /// <summary>
    /// The gameobject you're currently pushing on
    /// </summary>
    public GameObject WallObject { get; set; }

    /// <summary>
    /// Is the MoveHelper standing on a terrain too steep to stand on (Always false if IsOnGround false)
    /// </summary>
    [Sync] public bool IsSlipping { get; private set; } // TODO IMPLEMENT

    /// <summary>
    /// The MoveHelper is stuck and we can't get it out
    /// </summary>
    [Sync] public bool IsStuck { get; private set; }

    /// <summary>
    /// To avoid getting stuck due to imprecision we shrink the bounds before checking and compensate for it later
    /// </summary>
    public float SkinWidth => Math.Min(Math.Max(0.1f, TraceWidth * 0.05f), GroundStickDistance);

    public float AppliedWidth => TraceWidth * WorldScale.x; // The width of the MoveHelper in world units
    public float AppliedDepth => TraceWidth * WorldScale.y; // The depth of the MoveHelper in world units
    public float AppliedHeight => TraceShape == TraceType.Sphere ? AppliedWidth :
        TraceShape == TraceType.Bounds ? TraceBounds.Size.z * WorldScale.z : TraceHeight * WorldScale.z; // The height of the MoveHelper in world units
    private Vector3 _offset => (RotateWithGameObject ? WorldRotation.Up : Vector3.Up) * (TraceShape == TraceType.Sphere || TraceShape == TraceType.Bounds ? 0f : AppliedHeight / 2f); // The position of the MoveHelper in world units

    /// <summary>
    /// The bounds of this MoveHelper generated from the TraceWidth and TraceHeight
    /// </summary>
    public BBox Bounds { get; set; }
    private BBox _shrunkenBounds;
    private string[] _pushTags;
    private Vector3 _lastVelocity;
    private float _minimumTolerance => MathX.Clamp(Time.Delta / 2f, 0.005f, 0.1f); // Floating precision tolerance, too high if used inside of OnUpdate so tied to update rate

    /// <summary>
    /// If another MoveHelper moved at the same time and they're stuck, let this one know that the other already unstuck for us
    /// </summary>
    public ShrimpleCharacterController UnstuckTarget;

    public Action<ShrimpleCollisionResult> OnCollide { get; set; }
    public bool IsOnPlatform => IsOnGround && GroundStickEnabled && !IsSlipping && StickToPlatforms && GroundObject.IsValid();
    public Vector3 PlatformVelocity => IsOnPlatform ? GroundObject.GetComponent<Collider>()?.GetVelocityAtPoint(WorldPosition) ?? Vector3.Zero : Vector3.Zero;
    public bool IsOnSurfaceWithVelocity => IsOnGround && GroundStickEnabled && !IsSlipping && ApplySurfaceVelocity && GroundObject.IsValid();
    public Vector3 SurfaceVelocity => IsOnSurfaceWithVelocity ? GroundObject.GetComponent<Collider>()?.SurfaceVelocity * GroundObject.WorldRotation ?? Vector3.Zero : Vector3.Zero;
    public Vector3 GroundVelocity => PlatformVelocity + SurfaceVelocity;

    protected override void OnStart()
    {
        RebuildBounds();
        _pushTags = BuildPushTags();
    }

    protected override void DrawGizmos()
    {
        if (Gizmo.IsSelected)
        {
            Gizmo.GizmoDraw draw = Gizmo.Draw;
            draw.Color = Color.Blue;

            if (TraceShape == TraceType.Box)
                draw.LineBBox(Bounds.Translate(Vector3.Up * Bounds.Size.z / 2f));
            if (TraceShape == TraceType.Cylinder)
                draw.LineCylinder(Vector3.Zero, WorldRotation.Up * (Bounds.Maxs.z - Bounds.Mins.z), Bounds.Maxs.x, Bounds.Maxs.x, 24);
            if (TraceShape == TraceType.Sphere)
                draw.LineSphere(Vector3.Up * Bounds.Maxs.x, Bounds.Maxs.x);
            if (TraceShape == TraceType.Bounds)
                draw.LineBBox(TraceBounds);
        }
    }

    private BBox BuildBounds()
    {
        if (TraceShape == TraceType.Bounds)
            return new BBox(TraceBounds.Mins * GameObject.WorldScale, TraceBounds.Maxs * GameObject.WorldScale);

        var x = GameObject.WorldScale.x;
        var y = GameObject.WorldScale.y;
        var z = GameObject.WorldScale.z;

        var width = TraceWidth / 2f * x;
        var depth = TraceWidth / 2f * y;
        var halfHeight = TraceHeight / 2f * z;

        // For cylinders we want the bounds to start at Z = 0 and extend up to the full height
        if (TraceShape == TraceType.Cylinder)
            return new BBox(new Vector3(-width, -depth, 0f), new Vector3(width, depth, halfHeight * 2f));

        return new BBox(new Vector3(-width, -depth, -halfHeight), new Vector3(width, depth, halfHeight));
    }

    private void RebuildBounds()
    {
        Bounds = BuildBounds();
        _shrunkenBounds = Bounds.Grow(-SkinWidth);
    }

    private Vector3 BuildGravity() => UseSceneGravity ? Scene.PhysicsWorld.Gravity : UseVectorGravity ? VectorGravity : new Vector3(0f, 0f, Gravity);

    private string[] BuildPushTags()
    {
        return PushTagsWeight.Keys.ToArray();
    }

    /// <summary>
    /// Casts the current bounds from to and returns the scene trace result
    /// </summary>
    /// <param name="bounds"></param>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <returns></returns>
    public SceneTraceResult BuildTrace(BBox bounds, Vector3 from, Vector3 to)
    {
        SceneTrace builder = new SceneTrace(); // Empty trace builder

        if (TraceShape == TraceType.Box || TraceShape == TraceType.Bounds)
            builder = Game.SceneTrace.Box(bounds, from, to);
        if (TraceShape == TraceType.Cylinder)
            builder = Game.SceneTrace.Cylinder(bounds.Maxs.z - bounds.Mins.z, bounds.Maxs.x, from, to);
        if (TraceShape == TraceType.Sphere)
            builder = Game.SceneTrace.Sphere(bounds.Maxs.x, from + WorldRotation.Up * bounds.Maxs.x, to + WorldRotation.Up * bounds.Maxs.x);

        builder = builder
            .IgnoreGameObjectHierarchy(GameObject)
            .WithoutTags(IgnoreTags);

        if (RotateWithGameObject)
            builder = builder.Rotated(PhysicallySimulated ? Collider.WorldRotation : GameObject.WorldRotation);

        return builder.Run();
    }

    private SceneTraceResult BuildPushTrace(BBox bounds, Vector3 from, Vector3 to)
    {
        SceneTrace builder = new SceneTrace(); // Empty trace builder

        if (TraceShape == TraceType.Box || TraceShape == TraceType.Bounds)
            builder = Game.SceneTrace.Box(bounds, from, to);
        if (TraceShape == TraceType.Cylinder)
            builder = Game.SceneTrace.Cylinder(bounds.Maxs.z - bounds.Mins.z, bounds.Maxs.x, from, to);
        if (TraceShape == TraceType.Sphere)
            builder = Game.SceneTrace.Sphere(bounds.Maxs.x, from, to);

        builder = builder
            .IgnoreGameObjectHierarchy(GameObject)
            .WithAnyTags(_pushTags); // Check for only the push tags

        if (RotateWithGameObject)
            builder = builder.Rotated(GameObject.WorldRotation);

        return builder.Run();
    }

    /// <summary>
    /// Detach the MoveHelper from the ground and launch it somewhere (Units per second)
    /// </summary>
    /// <param name="amount"></param>
    public void Punch(in Vector3 amount)
    {
        Velocity += amount + GroundVelocity; // Apply before ungrounding, otherwise we lose the platform and surface velocities
        IsOnGround = false;

        // For physical mode, apply directly to the physics body
        if (PhysicallySimulated && Body.IsValid())
        {
            Body.Velocity += amount + GroundVelocity;
        }
    }

    protected void CreateBody()
    {
        // Destroy existing components
        if (Collider.IsValid())
            Collider.Destroy();
        if (Body.IsValid())
            Body.Destroy();
        if (BodyObject.IsValid())
            BodyObject.Destroy();

        // Create collider and rigidbody on the main GameObject
        // This ensures they're properly linked for physics interactions
        CreateCollider();
        CreateRigidbody();
    }

    protected void DestroyBody()
    {
        if (Collider.IsValid())
            Collider.Destroy();
        if (Body.IsValid())
            Body.Destroy();
        if (BodyObject.IsValid())
            BodyObject.Destroy();
    }

    protected void CreateCollider()
    {
        if (Collider.IsValid())
            Collider.Destroy();

        // Physical mode always uses capsule - rounded edges prevent snagging on terrain
        if (PhysicallySimulated)
        {
            Collider = GameObject.GetOrAddComponent<CapsuleCollider>();
        }
        else
        {
            // Non-physical mode uses the selected trace shape for the collider
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
        if (!Collider.IsValid())
            return;

        if (Collider is CapsuleCollider capsuleCollider)
        {
            // Capsule for Physical mode - rounded ends help glide over terrain
            var radius = (TraceWidth - SkinWidth) / 2f;
            var height = TraceHeight - SkinWidth;
            capsuleCollider.Radius = radius;
            // Start and End define the capsule spine (excluding the hemisphere caps)
            // Bottom at radius height (so hemisphere touches ground), top at height - radius
            capsuleCollider.Start = Vector3.Up * radius;
            capsuleCollider.End = Vector3.Up * (height - radius);
        }
        else if (Collider is BoxCollider boxCollider)
        {
            var bounds = BuildBounds();
            boxCollider.Scale = bounds.Size - SkinWidth;
            // Center the box so bottom is at feet (origin) - box bounds are centered, so offset up by half height
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
            // Center sphere at radius height so bottom touches feet
            sphereCollider.Center = Vector3.Up * sphereCollider.Radius;
        }
    }

    protected void CreateRigidbody()
    {
        if (Body.IsValid())
            Body.Destroy();

        if (GameObject.Components.TryGet<Rigidbody>(out var existingBody))
            existingBody.Destroy();

        Body = GameObject.AddComponent<Rigidbody>();
        Body.Locking = new PhysicsLock()
        {
            Pitch = true,
            Roll = true,
            Yaw = true,
        };
        // IMPORTANT: Disable rigidbody gravity - the controller handles gravity manually
        // This prevents double gravity application and maintains 1:1 parity with non-physical mode
        Body.Gravity = false;
        Body.MassOverride = BodyMassOverride;
        Body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;

        Body.EnhancedCcd = EnableCCD;

        if (HidePhysicalComponents)
            Body.Flags |= ComponentFlags.Hidden;
        else
            Body.Flags &= ~ComponentFlags.Hidden;

    }

    // Step handling for physical mode
    bool _didStep;
    Vector3 _stepPosition;
    const float StepSkin = 0.095f;

    void IScenePhysicsEvents.PrePhysicsStep()
    {
        if (!Body.IsValid()) return;

        // For non-physical mode with manual update, just return
        if (!PhysicallySimulated && ManuallyUpdate) return;

        // For non-physical automatic mode, run Move() normally
        if (!PhysicallySimulated)
        {
            Move();
            return;
        }

        // === PHYSICAL MODE ===
        // Following s&box PlayerController pattern: physics-driven with damping/friction control

        _didStep = false;

        // Update body physics - gravity, damping, friction
        UpdateBodyPhysics();

        // Update mass center - shift it up when moving to help glide over bumps
        UpdateMassCenter();

        // Add velocity towards wish direction
        AddWishVelocity();

        // Try stepping up obstacles
        if (StepsEnabled && IsOnGround)
        {
            TryStepUp();
        }
    }

    /// <summary>
    /// Updates the mass center based on movement - shifts it up when moving to help tip over bumps
    /// Following s&box PlayerController pattern from Elements.cs
    /// </summary>
    private void UpdateMassCenter()
    {
        if (!Body.IsValid()) return;

        // When moving, shift mass center up to waist level so physics body can "tip over" small bumps
        // When stationary, drop mass center to foot level for stability
        var wishSpeed = WishVelocity.WithZ(0).Length;
        var halfHeight = AppliedHeight * 0.5f;

        // Clamp wish speed contribution: 0 at rest, up to half height when moving fast
        float massCenter = IsOnGround ? wishSpeed.Clamp(0, halfHeight) : halfHeight;

        Body.MassCenterOverride = new Vector3(0, 0, massCenter);
        Body.OverrideMassCenter = true;
    }

    /// <summary>
    /// Updates body physics properties - gravity, damping, friction (following s&box MoveMode.UpdateRigidBody pattern)
    /// </summary>
    private void UpdateBodyPhysics()
    {
        if (!Collider.IsValid() || !Body.IsValid()) return;

        // Gravity control: only enable when moving or on dynamic ground
        bool wantsGravity = false;
        if (!IsOnGround) wantsGravity = true;
        if (Velocity.Length > 1f) wantsGravity = true;
        if (GroundVelocity.Length > 1f) wantsGravity = true;

        // Check if on dynamic ground
        var onDynamic = false;
        if (IsOnGround && GroundObject.IsValid())
        {
            var groundBody = GroundObject.GetComponent<Rigidbody>();
            onDynamic = groundBody != null && groundBody.PhysicsBody.BodyType == PhysicsBodyType.Dynamic;
        }
        if (onDynamic) wantsGravity = true;

        // We handle gravity manually in AddWishVelocity, so always disable body gravity
        // This prevents double gravity application
        Body.Gravity = false;

        // Linear damping for braking (s&box pattern)
        // Only apply high damping when stationary on non-moving ground
        var velocityHorizontal = Body.Velocity.WithZ(0).Length;
        bool wantsBrakes = IsOnGround && WishVelocity.Length < 1f && velocityHorizontal < 10f;
        Body.LinearDamping = wantsBrakes ? 10f : 0.1f;
        Body.AngularDamping = 1f;

        // Collider friction - low when moving to allow physics to handle collisions
        float friction = 0f;
        if (IsOnGround && WishVelocity.Length < 0.1f)
        {
            // Standing still - some friction to prevent sliding
            friction = onDynamic ? 0.5f : 1f;
        }
        Collider.Friction = friction;
    }

    /// <summary>
    /// Adds velocity towards wish direction (following s&box MoveMode.AddVelocity pattern)
    /// Ground velocity is handled separately in PostPhysicsStep for correct timing
    /// </summary>
    private void AddWishVelocity()
    {
        var wish = WishVelocity;
        var z = Body.Velocity.z;

        // Work with velocity directly (ground velocity handled in PostPhysicsStep)
        var velocity = Body.Velocity;

        // Calculate acceleration/deceleration using the same logic as non-physical mode
        var currentSpeed = Math.Max(velocity.WithZ(0).Length, 10f);
        var acceleration = (IsOnGround ? GroundAcceleration : AirAcceleration) * (FixedAcceleration ? 1f : AccelerationCurve.Evaluate(currentSpeed));
        var deceleration = (IsOnGround ? GroundDeceleration : AirDeceleration) * (FixedDeceleration ? 1f : DecelerationCurve.Evaluate(currentSpeed));

        // Apply ground surface friction if enabled
        if (!IgnoreGroundSurface && GroundSurface != null)
        {
            acceleration *= GroundSurface.Friction;
            deceleration *= GroundSurface.Friction;
        }

        if (!wish.IsNearZeroLength)
        {
            // We have input - accelerate towards wish
            if (AccelerationEnabled)
            {
                var speed = velocity.WithZ(0).Length;
                var maxSpeed = MathF.Max(wish.Length, speed);

                // Project wish velocity onto ground plane when grounded
                // This makes velocity follow the slope instead of being purely horizontal
                var targetVelocity = wish;
                if (IsOnGround && GroundStickEnabled && !GroundNormal.IsNearlyZero(0.01f))
                {
                    targetVelocity = Vector3.VectorPlaneProject(wish, GroundNormal);
                    // Maintain original speed after projection
                    if (!targetVelocity.IsNearlyZero(0.01f))
                    {
                        // Only apply slope slowdown when going uphill (against gravity)
                        // Check if the projected velocity is going upward
                        var isGoingUphill = Vector3.Dot(targetVelocity, AppliedGravity) < 0f;
                        var slopeMultiplier = isGoingUphill ? GetSlopeVelocityMultiplier(GroundAngle) : 1f;
                        targetVelocity = targetVelocity.Normal * maxSpeed * slopeMultiplier;
                    }
                }
                else
                {
                    targetVelocity = wish.Normal * maxSpeed;
                }

                // MoveTowards the projected target velocity (includes Z component for slope following)
                velocity = velocity.MoveTowards(targetVelocity, acceleration * Time.Delta);

                // Clamp speed to max
                if (velocity.Length > maxSpeed)
                    velocity = velocity.Normal * maxSpeed;
            }
            else
            {
                // Instant acceleration - directly use wish velocity
                // Project onto ground plane to follow slopes
                if (IsOnGround && GroundStickEnabled && !GroundNormal.IsNearlyZero(0.01f))
                {
                    var projectedWish = Vector3.VectorPlaneProject(wish, GroundNormal);
                    if (!projectedWish.IsNearlyZero(0.01f))
                    {
                        // Only apply slope slowdown when going uphill
                        var isGoingUphill = Vector3.Dot(projectedWish, AppliedGravity) < 0f;
                        var slopeMultiplier = isGoingUphill ? GetSlopeVelocityMultiplier(GroundAngle) : 1f;
                        velocity = projectedWish.Normal * wish.Length * slopeMultiplier;
                    }
                    else
                        velocity = Vector3.Zero;
                }
                else
                {
                    velocity = wish;
                }
            }
        }
        else if (IsOnGround)
        {
            // No input and on ground - decelerate to zero
            if (AccelerationEnabled)
            {
                velocity = velocity.MoveTowards(Vector3.Zero, deceleration * Time.Delta);
            }
            else
            {
                // Instant stop
                velocity = Vector3.Zero;
            }
        }

        // Preserve vertical velocity when grounded but NOT sticking to ground
        // When ground stick is enabled, velocity follows the slope so we don't preserve Z
        if (IsOnGround && !GroundStickEnabled)
        {
            velocity.z = z;
        }

        // Apply gravity when not grounded or when slipping
        if (GravityEnabled && (!IsOnGround || IsSlipping || !GroundStickEnabled))
        {
            velocity += AppliedGravity * Time.Delta;
        }

        // Set velocity directly (s&box pattern - physics handles collisions)
        Body.Velocity = velocity;
    }

    /// <summary>
    /// Try to step up an obstacle in front of us (following s&box PlayerController.Step.cs pattern)
    /// Uses wish velocity direction so we can step even when blocked (velocity is zero)
    /// </summary>
    private void TryStepUp()
    {
        // Use wish velocity for direction - this allows stepping even when blocked
        // Fall back to body velocity if no wish input
        var wishHorizontal = WishVelocity.WithZ(0);
        var bodyHorizontal = Body.Velocity.WithZ(0);

        // Prefer wish direction, but use body velocity if no input
        var moveDir = wishHorizontal.IsNearlyZero(0.1f) ? bodyHorizontal : wishHorizontal;
        if (moveDir.IsNearlyZero(0.1f))
            return;

        var from = WorldPosition;
        // Use a minimum check distance based on step depth, scaled by velocity if moving
        var speed = MathF.Max(bodyHorizontal.Length * Time.Delta, StepDepth);
        var vel = moveDir.Normal * speed;

        // 1. Trace forward in movement direction
        var a = from + _offset - vel.Normal * SkinWidth;
        var b = from + _offset + vel;

        var forwardTrace = BuildTrace(_shrunkenBounds, a, b);

        // If started solid, bail
        if (forwardTrace.StartedSolid)
            return;

        // If we didn't hit anything, no step needed
        if (!forwardTrace.Hit)
            return;

        // Calculate remaining distance after hit
        var remainingDist = vel.Length - forwardTrace.Distance;
        if (remainingDist <= 0)
            remainingDist = StepDepth; // Minimum step depth when right up against obstacle

        var remainingVel = vel.Normal * remainingDist;

        // 2. Move upward from hit point
        from = forwardTrace.EndPosition - _offset; // Convert back to feet position
        var upPoint = from + _offset + Vector3.Up * StepHeight;

        var upTrace = BuildTrace(_shrunkenBounds, from + _offset, upPoint);

        if (upTrace.StartedSolid)
            return;

        // Need at least 2 units of headroom
        if (upTrace.Distance < 2f)
            return;

        // 3. Move across at raised height
        var raisedPos = upTrace.EndPosition;
        var acrossEnd = raisedPos + remainingVel;

        var acrossTrace = BuildTrace(_shrunkenBounds, raisedPos, acrossEnd);

        if (acrossTrace.StartedSolid)
            return;

        // 4. Step down to find ground
        var top = acrossTrace.EndPosition;
        var bottom = top + Vector3.Down * StepHeight;

        var downTrace = BuildTrace(_shrunkenBounds, top, bottom);

        // No ground found
        if (!downTrace.Hit)
            return;

        // Check if standable
        var groundAngle = Vector3.GetAngle(Vector3.Up, downTrace.Normal);
        if (!IsAngleStandable(groundAngle))
            return;

        // Calculate new feet position
        var newFeetPos = downTrace.EndPosition - _offset + Vector3.Up * SkinWidth;

        // Didn't step up enough to matter (avoids jitter on flat ground)
        if (MathF.Abs(newFeetPos.z - WorldPosition.z) < 0.5f)
            return;

        // Perform the step
        _didStep = true;
        _stepPosition = newFeetPos;
        Body.WorldPosition = _stepPosition;
        Body.Velocity = Body.Velocity.WithZ(0) * 0.9f;
    }

    void IScenePhysicsEvents.PostPhysicsStep()
    {
        if (!Body.IsValid())
        {
            WorldPosition += GroundVelocity * Time.Delta;
            return;
        }

        if (!PhysicallySimulated) return;

        // === PHYSICAL MODE POST-PHYSICS ===

        // Restore step position if we stepped (prevents double velocity)
        if (_didStep)
        {
            Body.WorldPosition = _stepPosition;
        }

        // Stick to ground if needed
        if (IsOnGround && GroundStickEnabled && !IsSlipping)
        {
            StickToGround();
        }

        // Update ground detection FIRST so we have accurate ground info
        CategorizePhysicalGround();

        // Now apply ground velocity with fresh ground data
        ApplyGroundVelocity();

        // Sync velocity from physics - this is our "own" velocity without ground velocity
        // Ground velocity is applied via position, not body velocity, so no subtraction needed
        Velocity = Body.Velocity;
    }

    /// <summary>
    /// Applies platform and surface velocity by moving position directly
    /// Called in PostPhysicsStep after ground is categorized for correct timing
    /// </summary>
    private void ApplyGroundVelocity()
    {
        if (!IsOnGround || !GroundStickEnabled || IsSlipping)
            return;

        var groundVelocity = GroundVelocity;
        if (groundVelocity.IsNearZeroLength)
            return;

        // Move position directly by ground velocity - this ensures perfect platform following
        // Using position instead of velocity avoids accumulation issues
        Body.WorldPosition += groundVelocity * Time.Delta;
    }

    /// <summary>
    /// Stick to ground by lifting slightly and placing back down (s&box Reground pattern)
    /// </summary>
    private void StickToGround()
    {
        // Don't reground if body is sleeping (not moving)
        if (Body.PhysicsBody.Sleeping)
            return;

        var currentPosition = WorldPosition;

        // Trace from slightly above to below
        var from = currentPosition + _offset + Vector3.Up * 1f;
        var to = currentPosition + _offset + Vector3.Down * GroundStickDistance;

        var trace = BuildTrace(_shrunkenBounds, from, to);

        if (trace.StartedSolid)
            return;

        if (trace.Hit)
        {
            var surfaceAngle = Vector3.GetAngle(Vector3.Up, trace.Normal);
            if (!IsAngleStandable(surfaceAngle))
                return; // Not standable

            var targetFeetPos = trace.EndPosition - _offset + Vector3.Up * 0.01f;
            var delta = currentPosition - targetFeetPos;

            if (delta.IsNearlyZero(0.001f))
                return;

            Body.WorldPosition = targetFeetPos;

            // When stepping down, clear vertical velocity to avoid fall buildup
            if (delta.z > 0.01f)
            {
                Body.Velocity = Body.Velocity.WithZ(0);
            }
        }
    }

    /// <summary>
    /// Detect what ground we're standing on (s&box CategorizeGround pattern)
    /// </summary>
    private void CategorizePhysicalGround()
    {
        var wasOnGround = IsOnGround;

        // Use a small sphere trace from feet position downward
        // This avoids the capsule hitting tilted objects to the side
        var feetPos = WorldPosition;
        var from = feetPos + Vector3.Up * 2f;
        var to = feetPos + Vector3.Down * 2f;

        // Use a small sphere instead of full capsule for ground detection
        var groundTrace = Game.SceneTrace.Sphere(SkinWidth * 2f, from, to)
            .IgnoreGameObjectHierarchy(GameObject)
            .WithoutTags(IgnoreTags)
            .Run();

        if (groundTrace.StartedSolid)
        {
            IsStuck = true;
            // Still try to detect ground with full trace as fallback
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
                // Surface too steep
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

    public void OnCollisionStart(Collision collision)
    {
        // DebugOverlay.Sphere(new Sphere(collision.Contact.Point, 5f), Color.Red, 10f);
    }

    /// <summary>
    /// Apply the WishVelocity, update the Velocity and the Position of the GameObject by simulating the MoveHelper
    /// </summary>
    /// <param name="manualUpdate">Just calculate but don't update position</param>
    public MoveHelperResult Move(bool manualUpdate = false) => Move(Time.Delta, manualUpdate);

    /// <summary>
    /// Apply the WishVelocity, update the Velocity and the Position of the GameObject by simulating the MoveHelper
    /// </summary>
    /// <param name="delta">The time step</param>
    /// <param name="manualUpdate">Just calculate but don't update position</param>
    public MoveHelperResult Move(float delta, bool manualUpdate = false)
    {
        var goalVelocity = CalculateGoalVelocity(delta); // Calculate the goal velocity using our Acceleration and Deceleration values

        // KNOWN ISSUE: Velocity starts to build up to massive amounts when trying to climb terrain too steep?

        // SIMULATE PUSH FORCES //
        if (PushEnabled)
        {
            var pushTrace = BuildPushTrace(Bounds, WorldPosition + _offset, WorldPosition); // Build a trace but using the Push tags instead of the Ignore tags

            if (pushTrace.Hit) // We're inside any of the push tags
            {
                foreach (var tag in pushTrace.GameObject.Tags)
                {
                    if (PushTagsWeight.TryGetValue(tag, out var tagWeight))
                    {
                        var otherPosition = pushTrace.GameObject.WorldPosition.WithZ(WorldPosition.z); // Only horizontal pushing
                        var pushDirection = (otherPosition - WorldPosition).Normal;
                        var pushVelocity = pushDirection * tagWeight * 50f; // I find 50 u/s to be a good amount to push if the weight is 1.0 (!!!)

                        goalVelocity -= pushVelocity;
                    }
                }
            }
        }

        var finalPosition = WorldPosition;

        var moveHelperResult = CollideAndSlide(goalVelocity, finalPosition + _offset, delta); // Simulate the MoveHelper

        finalPosition = moveHelperResult.Position;
        var finalVelocity = moveHelperResult.Velocity;
        // SIMULATE GRAVITY //
        if (GravityEnabled && Gravity != 0f)
        {
            if (!IsOnGround || IsSlipping || !GroundStickEnabled)
            {
                var gravity = AppliedGravity * delta;
                var gravityResult = CollideAndSlide(gravity, moveHelperResult.Position, delta, gravityPass: true); // Apply and simulate the gravity step

                finalPosition = gravityResult.Position;
                finalVelocity += gravityResult.Velocity;
            }
        }

        if (!ExternalVelocity.IsNearZeroLength)
        {
            moveHelperResult = CollideAndSlide(ExternalVelocity, finalPosition, delta);
        }

        finalPosition = moveHelperResult.Position - _offset; // Compensate for the offset we added at the beginning
        _lastVelocity = Velocity * delta;

        if (!manualUpdate)
        {
            Velocity = finalVelocity;
            WorldPosition = finalPosition; // Actually updating the position is "expensive" so we only do it once at the end
        }

        return new MoveHelperResult(finalPosition, finalVelocity, moveHelperResult.Offset);
    }

    /// <summary>
    /// Sometimes we have to update only the position but not the velocity (Like when climbing steps or getting unstuck) so we can't have Position rely only on Velocity
    /// </summary>
    public struct MoveHelperResult
    {
        public Vector3 Position;
        public Vector3 Velocity;
        internal Vector3 Offset;
        internal float Leftover = 1f;

        public MoveHelperResult(Vector3 position, Vector3 velocity)
        {
            Position = position;
            Velocity = velocity;
        }

        internal MoveHelperResult(Vector3 position, Vector3 velocity, Vector3 offset, float leftover = 1f)
        {
            Position = position;
            Velocity = velocity;
            Offset = offset;
            Leftover = leftover;
        }
    }

    bool _bounced = false;

    private MoveHelperResult CollideAndSlide(Vector3 velocity, Vector3 position, float delta, int depth = 0, bool gravityPass = false) =>
        CollideAndSlide(new MoveHelperResult(position, velocity), delta, depth, gravityPass);

    private MoveHelperResult CollideAndSlide(MoveHelperResult current, float delta, int depth = 0, bool gravityPass = false)
    {
        if (depth >= MaxBounces)
            return current;

        var velocity = current.Velocity * delta; // I like to set Velocity as units/second but we have to deal with units/tick here
        var position = current.Position;

        // GROUND AND UNSTUCK CHECK //
        if (depth == 0) // Only check for the first step since it's impossible to get stuck on other steps
        {
            var groundTrace = BuildTrace(_shrunkenBounds, position, position + AppliedGravity.Normal * (GroundStickDistance + SkinWidth * 1.1f)); // Compensate for floating inaccuracy

            if (groundTrace.StartedSolid)
            {
                IsStuck = true;
                if (UnstuckEnabled)
                {
                    if (PhysicallySimulated)
                    {
                        // For physical mode, attempt physics-aware unstuck
                        if (Body.IsValid() && TryPhysicalUnstuck(position, out var physResult))
                        {
                            IsStuck = false;
                            position = physResult;
                        }
                        // If physical unstuck fails, IsStuck remains true
                        // The physics body may naturally resolve this through collision response
                    }
                    else if (UnstuckTarget == null)
                    {
                        IsStuck = !TryUnstuck(position, out var result);

                        if (!IsStuck)
                        {
                            position = result; // Update the new position

                            if (groundTrace.GameObject != null)
                                if (groundTrace.GameObject.Components.TryGet<ShrimpleCharacterController>(out var otherHelper))
                                    otherHelper.UnstuckTarget = this; // We already solved this, no need to unstuck the other helper
                        }
                        else
                        {
                            return new MoveHelperResult(position, Vector3.Zero); // Mission failed, bail out!
                        }
                    }
                    else
                    {
                        UnstuckTarget = null; // Alright the other MoveHelper got us unstuck so just do nothing
                    }
                }
            }
            else
            {
                var hasLanded = !IsOnGround && Vector3.Dot(Velocity, AppliedGravity) >= 0f && groundTrace.Hit && groundTrace.Distance <= SkinWidth * 2f; // Wasn't on the ground and now is
                var isGrounded = IsOnGround && groundTrace.Hit; // Was already on the ground and still is, this helps stick when going down stairs

                IsOnGround = hasLanded || isGrounded;
                GroundSurface = IsOnGround ? groundTrace.Surface : null;
                GroundNormal = IsOnGround ? groundTrace.Normal : -AppliedGravity.Normal;
                GroundObject = IsOnGround ? groundTrace.GameObject : null;
                IsSlipping = IsOnGround && !IsAngleStandable(GroundAngle);

                if (IsSlipping && !gravityPass && Vector3.Dot(velocity, AppliedGravity) < 0f)
                    velocity = velocity.WithZ(0f); // If we're slipping ignore any extra velocity we had

                // FIX: Clamp velocity when slipping to prevent buildup
                // Project velocity onto the slope and limit accumulation
                if (IsSlipping)
                {
                    var slopeDir = Vector3.VectorPlaneProject(AppliedGravity.Normal, GroundNormal).Normal;
                    var slopeSpeed = Vector3.Dot(velocity, slopeDir);
                    var maxSlipSpeed = AppliedGravity.Length * delta * 2f; // Reasonable max slip speed per frame

                    if (slopeSpeed > maxSlipSpeed)
                    {
                        // Clamp the velocity component along the slope
                        velocity = velocity - slopeDir * (slopeSpeed - maxSlipSpeed);
                    }
                }

                if (IsOnGround && GroundStickEnabled && !IsSlipping)
                {
                    position = groundTrace.EndPosition + -AppliedGravity.Normal * SkinWidth; // Place on the ground
                    velocity = Vector3.VectorPlaneProject(velocity, GroundNormal); // Follow the ground you're on without projecting Z
                }

                IsStuck = false;
            }
        }

        if (velocity.IsNearlyZero(_minimumTolerance)) // Not worth continuing, reduces small stutter
        {
            return new MoveHelperResult(position, Vector3.Zero, current.Offset);
        }

        var toTravel = velocity.Length * current.Leftover + SkinWidth;
        var targetPosition = position + velocity.Normal * toTravel;
        var travelTrace = BuildTrace(_shrunkenBounds, position, targetPosition);

        if (travelTrace.Hit)
        {
            var travelled = velocity.Normal * Math.Max(travelTrace.Distance - SkinWidth, 0f);
            var leftover = velocity - travelled; // How much leftover velocity still needs to be simulated
            var speed = leftover.Length;
            var angle = Vector3.GetAngle(-AppliedGravity.Normal, travelTrace.Normal);

            if (toTravel >= SkinWidth && travelTrace.Distance < SkinWidth)
                travelled = Vector3.Zero;

            var elasticityDirection = MathX.Lerp(HorizontalElasticity, VerticalElasticity, angle / 90f);
            var elasticity = PhysicallySimulated ? 0f : BouncingEnabled ?
                velocity.Length / delta <= ElasticityThreshold ?
                    0f : elasticityDirection * (IncludeGroundElasticity ? GroundSurface?.Elasticity ?? 1f : 1f)
                : 0f; // Allah help me format this better

            if (IsAngleStandable(angle)) // Terrain we can walk on
            {
                var projectedLeftover = leftover;

                if (gravityPass || !IsOnGround)
                    projectedLeftover = Vector3.VectorPlaneProject(projectedLeftover, travelTrace.Normal); // Don't project the vertical velocity after landing else it boosts your horizontal velocity
                else
                    projectedLeftover = projectedLeftover.ProjectAndScale(travelTrace.Normal); // Project the velocity along the terrain

                // Apply slope velocity reduction only when going uphill (against gravity)
                var isGoingUphill = Vector3.Dot(projectedLeftover, AppliedGravity) < 0f;
                if (isGoingUphill)
                {
                    var slopeMultiplier = GetSlopeVelocityMultiplier(angle);
                    projectedLeftover *= slopeMultiplier;
                }

                if (elasticity > 0)
                    leftover = Vector3.Lerp(projectedLeftover, Vector3.Reflect(leftover, travelTrace.Normal), elasticity, false);
                else
                    leftover = projectedLeftover;

                IsPushingAgainstWall = false;
                WallObject = null;
            }
            else
            {
                var climbedStair = false;

                if (angle >= 90f - WallTolerance && angle <= 90f + WallTolerance) // Check for walls
                    IsPushingAgainstWall = true; // We're pushing against a wall

                if (StepsEnabled)
                {
                    var isStep = angle >= 90f - StepTolerance && angle <= 90f + StepTolerance;

                    if (isStep || PseudoStepsEnabled) // Check for steps
                    {
                        if (IsOnGround) // Stairs VVV
                        {
                            var stepHorizontal = Vector3.VectorPlaneProject(velocity.Normal, AppliedGravity.Normal).Normal * StepDepth; // How far in front we're looking for steps
                            var stepVertical = -AppliedGravity.Normal * (StepHeight + SkinWidth); // How high we're looking for steps + Some to compensate for floating inaccuracy
                            var stepTrace = BuildTrace(_shrunkenBounds, travelTrace.EndPosition + stepHorizontal + stepVertical, travelTrace.EndPosition + stepHorizontal);
                            var stepAngle = Vector3.GetAngle(stepTrace.Normal, -AppliedGravity.Normal);

                            if (!stepTrace.StartedSolid && stepTrace.Hit && IsAngleStandable(stepAngle)) // We found a step!
                            {
                                if (isStep || !IsSlipping && PseudoStepsEnabled)
                                {
                                    var stepDistance = stepTrace.EndPosition - travelTrace.EndPosition;
                                    var stepTravelled = -AppliedGravity.Normal * stepDistance;
                                    position += stepTravelled; // Offset our position by the height of the step climbed
                                    climbedStair = true;

                                    IsPushingAgainstWall = false; // Nevermind, we're not against a wall, we climbed a step!
                                    WallObject = null;
                                }
                            }
                        }
                    }
                }

                if (IsPushingAgainstWall)
                {
                    // Scale our leftover velocity based on the angle of approach relative to the wall
                    // (Perpendicular = 0%, Parallel = 100%)
                    var scale = ScaleAgainstWalls ? 1f - Vector3.Dot(-travelTrace.Normal.Normal / GripFactorReduction, velocity.Normal) : 1f;
                    var wallLeftover = ScaleAgainstWalls ? Vector3.VectorPlaneProject(leftover, travelTrace.Normal.Normal) : leftover.ProjectAndScale(travelTrace.Normal.Normal);

                    if (elasticity > 0)
                        leftover = Vector3.Lerp(wallLeftover, Vector3.Reflect(leftover, travelTrace.Normal), elasticity, false);
                    else
                        leftover = (wallLeftover * scale).WithZ(wallLeftover.z);

                    WallObject = travelTrace.GameObject;
                    WallNormal = travelTrace.Normal;
                }
                else
                {
                    if (!climbedStair)
                    {
                        var scale = IsSlipping ? 1f : 1f - Vector3.Dot(-travelTrace.Normal / GripFactorReduction, velocity.Normal);
                        var wallLeftover = ScaleAgainstWalls ? Vector3.VectorPlaneProject(leftover, travelTrace.Normal) * scale : leftover.ProjectAndScale(travelTrace.Normal);

                        if (elasticity > 0)
                            leftover = Vector3.Lerp(wallLeftover, Vector3.Reflect(leftover, travelTrace.Normal), elasticity, false);
                        else
                            leftover = wallLeftover;
                    }

                }


            }

            var previousVelocity = velocity;
            var leftoverSpeed = leftover.Length / speed; // How much speed we had left after the collision
            velocity = leftover.Normal * velocity.Length * leftoverSpeed;

            if (travelled.Length <= _minimumTolerance && leftover.Length <= _minimumTolerance)
                return new MoveHelperResult(position + travelled, velocity / delta, current.Offset);

            if (elasticity > 0)
                _bounced = true;

            var offset = position - current.Position;
            var newResult = CollideAndSlide(new MoveHelperResult(position + travelled, velocity / delta, offset, leftover.Length / velocity.Length), delta, depth + 1, gravityPass); // Simulate another bounce for the leftover velocity from the latest position

            if (depth == 0 && !gravityPass)
            {
                var collision = new ShrimpleCollisionResult()
                    .WithHitNormal(travelTrace.Normal)
                    .WithHitPosition(travelTrace.HitPosition)
                    .WithAngle(angle)
                    .WithHitObject(travelTrace.GameObject)
                    .WithHitSurface(travelTrace.Surface)
                    .WithHitVelocityBefore(previousVelocity / delta)
                    .WithHitVelocityAfter(newResult.Velocity);

                OnCollide?.Invoke(collision);
            }

            return newResult;
        }

        if (depth == 0 && !gravityPass)
        {
            IsPushingAgainstWall = false;
            WallObject = null;
        }

        if (gravityPass && _bounced)
        {
            _bounced = false;
            velocity -= AppliedGravity * delta * delta * 1.4f; // Evil hack so it doesn't lose velocity due to gravity passes after each bounce
        }

        return new MoveHelperResult(position + velocity, velocity / delta, current.Offset); // We didn't hit anything? Ok just keep going then :-)
    }

    private float CalculateGoalSpeed(Vector3 wishVelocity, Vector3 velocity, bool isAccelerating, float delta)
    {
        if (!AccelerationEnabled) return 999999999f; // Should be enough for the freaks that don't want acceleration

        float goalSpeed;

        var isSameDirection = velocity.IsNearlyZero(1f) || Vector3.Dot(wishVelocity.WithZ(0f).Normal, velocity.WithZ(0f).Normal) >= 0f; // Is our wishVelocity roughly moving towards our velocity already?
        var currentSpeed = Math.Max(Velocity.WithZ(0f).Length, 10f);
        var acceleration = (IsOnGround ? GroundAcceleration : AirAcceleration) * (FixedAcceleration ? 1f : AccelerationCurve.Evaluate(currentSpeed));
        var deceleration = (IsOnGround ? GroundDeceleration : AirDeceleration) * (FixedDeceleration ? 1f : DecelerationCurve.Evaluate(currentSpeed));

        if (isAccelerating)
            goalSpeed = acceleration;
        else
            goalSpeed = !isSameDirection ? Math.Max(acceleration, deceleration) : deceleration; // Makes movement more responsive especially for flying or rolling

        if (!IgnoreGroundSurface && GroundSurface != null)
            goalSpeed *= GroundSurface.Friction; // Take into account the ground's friction

        goalSpeed *= delta;

        return goalSpeed;
    }

    private Vector3 CalculateGoalVelocity(float delta)
    {
        bool shouldIgnoreZ = IgnoreZ || (IgnoreZWhenZero && WishVelocity.z.AlmostEqual(0f));
        var wishVelocity = shouldIgnoreZ ? (WishVelocity.Normal * WishVelocity.Length).WithZ(Velocity.z) : WishVelocity;
        var isAccelerating = shouldIgnoreZ ? wishVelocity.WithZ(0f).Length >= Velocity.WithZ(0f).Length : wishVelocity.Length >= Velocity.Length;

        var goalSpeed = CalculateGoalSpeed(wishVelocity, Velocity, isAccelerating, delta);
        var goalVelocity = Velocity.MoveTowards(wishVelocity, goalSpeed);

        return shouldIgnoreZ ? goalVelocity.WithZ(Velocity.z) : goalVelocity;
    }

    /// <summary>
    /// Attempts to return the nearest standable position until you're not longer inside other colliders<br/>
    /// If it fails too frequently, consider increasing <see cref="MaxUnstuckTries"/>
    /// </summary>
    /// <param name="position">The start position</param>
    /// <param name="result">The final standable position</param>
    /// <returns>True if it found one, false if it failed.</returns>
    public bool TryUnstuck(Vector3 position, out Vector3 result)
    {
        if (_lastVelocity == Vector3.Zero)
            _lastVelocity = -AppliedGravity.Normal;

        var velocityLength = _lastVelocity.Length + SkinWidth;
        var startPos = position - _lastVelocity.Normal * velocityLength; // Try undoing the last velocity 1st
        var endPos = position;

        for (int i = 0; i < MaxUnstuckTries + 1; i++)
        {
            if (i == 1)
                startPos = position + -AppliedGravity.Normal * 2f; // Try going up 2nd

            if (i > 1 && i < MaxUnstuckTries / 2f)
                startPos = position + Vector3.Random.Normal * ((float)i / 2f); // Start randomly checking 3rd

            if (i >= MaxUnstuckTries / 2f)
                startPos = position + Vector3.Random.Normal * i; // Ok at this point start checking further

            if (startPos - endPos == Vector3.Zero) // No difference!
                continue;

            var unstuckTrace = BuildTrace(_shrunkenBounds, startPos, endPos);

            if (!unstuckTrace.StartedSolid)
            {
                result = unstuckTrace.EndPosition - _lastVelocity.Normal * SkinWidth / 4f;
                _lastVelocity = Vector3.Zero;
                return true;
            }
        }

        result = position;
        return false;
    }

    /// <summary>
    /// Physics-aware unstuck for Physical mode. Uses the physics body position as a potential escape route.
    /// </summary>
    /// <param name="position">The start position</param>
    /// <param name="result">The final standable position</param>
    /// <returns>True if found a valid position, false otherwise.</returns>
    public bool TryPhysicalUnstuck(Vector3 position, out Vector3 result)
    {
        // First, check if the physics body has already pushed us to a valid position
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

        // Try pushing upward (most common escape direction)
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

        // Try the physics body's velocity direction
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

    /// <summary>
    /// Debug don't use
    /// </summary>
    /// <param name="position"></param>
    /// <param name="title"></param>
    /// <returns></returns>
    private bool TestPosition(Vector3 position, string title)
    {
        var testTrace = BuildTrace(_shrunkenBounds, position, position);

        if (testTrace.StartedSolid)
        {
            Log.Info($"[{RealTime.Now}]{title} {GameObject.Name} started solid at {position} against {testTrace.GameObject}");
            return true;
        }

        return false;
    }

    protected override void OnFixedUpdate()
    {
        base.OnFixedUpdate();

        if (!ManuallyUpdate && Active && !PhysicallySimulated) // If we're not physically simulated we Move inside of OnFixedUpdate
            Move();
    }

    public override int ComponentVersion => 4;

    [JsonUpgrader(typeof(ShrimpleCharacterController), 3)]
    private static void UpdateTraceShape(JsonObject json)
    {
        if (json.Remove("CylinderTrace", out var newNode) && newNode is JsonValue boolNode && boolNode.TryGetValue<bool>(out var isCylinder))
            json["TraceShape"] = isCylinder ? "Cylinder" : "Box";
        else
            json["TraceShape"] = "Box";
    }

    [JsonUpgrader(typeof(ShrimpleCharacterController), 4)]
    private static void UpdateMaxGroundAngle(JsonObject json)
    {
        // Old default was 60f, new default is (30f, 60f)
        if (json.TryGetPropertyValue("MaxGroundAngle", out var oldValue) && oldValue is JsonValue floatValue && floatValue.TryGetValue<float>(out var oldAngle))
        {
            json.Remove("MaxGroundAngle");
            var startAngle = oldAngle * 0.75f;
            json["MaxGroundAngle"] = $"{startAngle:F2} {oldAngle:F2}";
        }
    }
}
