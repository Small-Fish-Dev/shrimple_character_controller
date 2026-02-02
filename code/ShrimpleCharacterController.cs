using System.Text.Json.Nodes;

namespace ShrimpleCharacterController;

[Icon("nordic_walking")]
public partial class ShrimpleCharacterController : Component, IScenePhysicsEvents, ISceneEvent<IScenePhysicsEvents>, Component.ICollisionListener
{
    /// <summary>
    /// Manually update this by calling Move() or let it always be simulated.
    /// Not supported when PhysicallySimulated is enabled.
    /// </summary>
    [Property]
    [Group("Options")]
    [HideIf(nameof(PhysicallySimulated), true)]
    public bool ManuallyUpdate { get; set; } = false;

    [Property]
    [FeatureEnabled("Physical")]
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
        var groundVel = GroundVelocity; // Capture before ungrounding
        IsOnGround = false;

        // For physical mode, apply directly to the physics body
        if (PhysicallySimulated && Body.IsValid())
        {
            // Set velocity to current + punch + ground velocity
            // This ensures we carry platform momentum when jumping
            Body.Velocity = Body.Velocity + amount + groundVel;
        }

        Velocity += amount + groundVel;
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

    protected override void OnDestroy()
    {
        base.OnDestroy();

        Collider?.Destroy();
        Body?.Destroy();
    }

    protected override void OnDisabled()
    {
        base.OnDisabled();

        Collider?.Enabled = false;
        Body?.Enabled = false;
    }

    protected override void OnEnabled()
    {
        base.OnEnabled();

        Collider?.Enabled = true;
        Body?.Enabled = true;
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
