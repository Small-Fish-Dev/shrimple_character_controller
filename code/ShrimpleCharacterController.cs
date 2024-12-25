namespace ShrimpleCharacterController;

[Icon("nordic_walking")]
public class ShrimpleCharacterController : Component
{
    /// <summary>
    /// Manually update this by calling Move() or let it always be simulated
    /// </summary>
    [Property]
    [Group("Options")]
    public bool ManuallyUpdate { get; set; } = true;

    /// <summary>
    /// If pushing against a wall, scale the velocity based on the wall's angle (False is useful for NPCs that get stuck on corners)
    /// </summary>
    [Property]
    [Group("Options")]
    public bool ScaleAgainstWalls { get; set; } = true;

    [Sync]
    float _traceWidth { get; set; } = 16f;

    /// <summary>
    /// Width of our trace
    /// </summary>
    [Property]
    [Group("Trace")]
    [Range(1f, 64f, 1f, true, true)]
    public float TraceWidth
    {
        get => _traceWidth;
        set
        {
            _traceWidth = value;
            Bounds = BuildBounds();
            _shrunkenBounds = Bounds.Grow(-SkinWidth);
        }
    }

    [Sync]
    float _traceHeight { get; set; } = 72f;

    /// <summary>
    /// Height of our trace
    /// </summary>
    [Property]
    [Group("Trace")]
    [Range(1f, 256f, 1f, true, true)]
    public float TraceHeight
    {
        get => _traceHeight;
        set
        {
            _traceHeight = value;
            Bounds = BuildBounds();
            _shrunkenBounds = Bounds.Grow(-SkinWidth);
        }
    }

    /// <summary>
    /// Which tags it should ignore
    /// </summary>
    [Property]
    [Group("Trace")]
    public TagSet IgnoreTags { get; set; } = new TagSet();

    /// <summary>
    /// Max amount of trace calls whenever the simulation doesn't reach its target (Slide and collide bounces)
    /// </summary>
    [Property]
    [Group("Trace")]
    [Range(1, 20, 1, true, true)]
    public int MaxBounces { get; set; } = 5;

    /// <summary>
    /// How fast you accelerate while on the ground (Units per second)
    /// </summary>
    [Property]
    [Group("Movement")]
    [Range(0f, 3000f, 10f, false)]
    [HideIf("GroundStickEnabled", false)]
    public float GroundAcceleration { get; set; } = 1000f;

    /// <summary>
    /// How fast you decelerate while on the ground (Units per second)
    /// </summary>
    [Property]
    [Group("Movement")]
    [Range(0f, 3000f, 10f, false)]
    [HideIf("GroundStickEnabled", false)]
    public float GroundDeceleration { get; set; } = 1500f;

    /// <summary>
    /// How fast you accelerate while in the air (Units per second)
    /// </summary>
    [Property]
    [Group("Movement")]
    [Range(0f, 3000f, 10f, false)]
    public float AirAcceleration { get; set; } = 300f;

    /// <summary>
    /// How fast you decelerate while in the air (Units per second)
    /// </summary>
    [Property]
    [Group("Movement")]
    [Range(0f, 3000f, 10f, false)]
    public float AirDeceleration { get; set; } = 0f;

    /// <summary>
    /// Do we ignore the friction of the surface you're standing on or not?
    /// </summary>
    [Property]
    [Group("Movement")]
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
    [Range(0f, 10f, 0.1f, false)]
    public float WallTolerance { get; set; } = 1f;

    /// <summary>
    /// Player feels like it's gripping walls too much? Try more Grip Factor Reduction!
    /// </summary>
    [Group("Movement")]
    [Property]
    [Range(1f, 10f, 0.1f, true)]
    public float GripFactorReduction { get; set; } = 1f;

    /// <summary>
    /// Stick the MoveHelper to the ground (IsOnGround will default to false if disabled)
    /// </summary>
    [FeatureEnabled("GroundStick")]
    [Property]
    public bool GroundStickEnabled { get; set; } = true;

    /// <summary>
    /// How steep terrain can be for you to stand on without slipping
    /// </summary>
    [Property]
    [Feature("GroundStick")]
    [Range(0f, 89f, 1f, true, true)]
    public float MaxGroundAngle { get; set; } = 60f;

    /// <summary>
    /// How far from the ground the MoveHelper is going to stick (Useful for going down stairs!)
    /// </summary>
    [Property]
    [Feature("GroundStick")]
    [Range(1f, 32f, 1f, false)]
    public float GroundStickDistance { get; set; } = 12f;

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
    [Range(1f, 32f, 1f, false)]
    public float StepHeight { get; set; } = 12f;

    /// <summary>
    /// How deep it checks for steps (Minimum depth)
    /// </summary>
    [Feature("Steps")]
    [Property]
    [Range(0.1f, 8f, 0.1f, false)]
    public float StepDepth { get; set; } = 2f;

    /// <summary>
    /// Tolerance from a 90° surface before it's considered a valid step (Ex. Tolerance 1 = Between 89° and 91° can be a step, 0.1 = 89.9° to 90.1°)
    /// </summary>
    [Feature("Steps")]
    [Property]
    [Range(0f, 10f, 0.1f, false)]
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

    [Sync]
    Dictionary<string, float> _pushTagsWeight { get; set; } = new Dictionary<string, float>() { { "player", 1f } };

    /// <summary>
    /// Which tags will push this MoveHelper away and with how much force (Make sure they are also included in IgnoreTags!) (+1 Trace call)
    /// </summary>
    [Property]
    [Feature("Push")]
    public Dictionary<string, float> PushTagsWeight
    {
        get => _pushTagsWeight;
        set
        {
            _pushTagsWeight = value;
            _pushTags = BuildPushTags();
        }
    }

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
    public bool UseSceneGravity { get; set; } = true;

    /// <summary>
    /// Units per second squared (Default is -850f)
    /// </summary>
    [Property]
    [Feature("Gravity")]
    [Range(-2000, 2000, 1, false)]
    [HideIf("UseSceneGravity", true)]
    public float Gravity { get; set; } = -850f;

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
    [Range(1, 50, 1, false)]
    public int MaxUnstuckTries { get; set; } = 20;

    /// <summary>
    /// The simulated target velocity for our MoveHelper (Units per second, we apply Time.Delta inside)
    /// </summary>
    [Sync] public Vector3 WishVelocity { get; set; }

    /// <summary>
    /// The resulting velocity after the simulation is done (Units per second)
    /// </summary>
    [Sync] public Vector3 Velocity { get; set; }

    /// <summary>
    /// Is the MoveHelper currently touching the ground
    /// </summary>
    [Sync] public bool IsOnGround { get; set; }

    /// The current ground normal you're standing on (Always Vector3.Zero if IsOnGround false)
    /// </summary>
    public Vector3 GroundNormal { get; private set; } = Vector3.Zero;

    /// <summary>
    /// The current ground angle you're standing on (Always 0f if IsOnGround false)
    /// </summary>
    public float GroundAngle => Vector3.GetAngle(GroundNormal, Vector3.Up);

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
    public float SkinWidth;

    /// <summary>
    /// The bounds of this MoveHelper generated from the TraceWidth and TraceHeight
    /// </summary>
    public BBox Bounds { get; set; }
    private BBox _shrunkenBounds;
    private string[] _pushTags;
    private Vector3 _lastVelocity;

    /// <summary>
    /// If another MoveHelper moved at the same time and they're stuck, let this one know that the other already unstuck for us
    /// </summary>
    public ShrimpleCharacterController UnstuckTarget;

    protected override void OnStart()
    {
        SkinWidth = Math.Min(Math.Max(0.1f, TraceWidth * 0.05f), GroundStickDistance); // SkinWidth is 5% of the total width
        Bounds = BuildBounds();
        _shrunkenBounds = Bounds.Grow(-SkinWidth);
        _pushTags = BuildPushTags();
    }

    protected override void DrawGizmos()
    {
        if (Gizmo.IsSelected)
        {
            Gizmo.GizmoDraw draw = Gizmo.Draw;
            draw.Color = Color.Blue;
            draw.LineBBox(BuildBounds());
        }
    }

    private BBox BuildBounds()
    {
        return new BBox(new Vector3(-TraceWidth / 2, -TraceWidth / 2f, 0f), new Vector3(TraceWidth / 2f, TraceWidth / 2f, TraceHeight));
    }

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
        return Game.SceneTrace.Box(bounds, from, to)
            .IgnoreGameObjectHierarchy(GameObject)
            .WithoutTags(IgnoreTags)
            .Run();
    }

    private SceneTraceResult BuildPushTrace(BBox bounds, Vector3 from, Vector3 to)
    {
        return Game.SceneTrace.Box(bounds, from, to)
            .IgnoreGameObjectHierarchy(GameObject)
            .WithAnyTags(_pushTags) // Check for only the push tags
            .Run();
    }

    /// <summary>
    /// Detach the MoveHelper from the ground and launch it somewhere (Units per second)
    /// </summary>
    /// <param name="amount"></param>
    public void Punch(in Vector3 amount)
    {
        if (amount.z < 10f) return; // Get out with that weak crap

        IsOnGround = false;
        Velocity += amount;

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
            var pushTrace = BuildPushTrace(Bounds, WorldPosition, WorldPosition); // Build a trace but using the Push tags instead of the Ignore tags

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

        var moveHelperResult = CollideAndSlide(goalVelocity, WorldPosition, delta); // Simulate the MoveHelper

        var finalPosition = moveHelperResult.Position;
        var finalVelocity = moveHelperResult.Velocity;

        // SIMULATE GRAVITY //
        if (GravityEnabled && Gravity != 0f)
        {
            if (!IsOnGround || IsSlipping || !GroundStickEnabled)
            {
                var gravityAmount = UseSceneGravity ? Scene.PhysicsWorld.Gravity.z : Gravity;
                var gravity = gravityAmount * Vector3.Up * delta;
                var gravityResult = CollideAndSlide(gravity, moveHelperResult.Position, delta, gravityPass: true); // Apply and simulate the gravity step

                finalPosition = gravityResult.Position;
                finalVelocity += gravityResult.Velocity;
            }
        }

        _lastVelocity = Velocity * delta;

        if (!manualUpdate)
        {
            Velocity = finalVelocity;
            WorldPosition = finalPosition; // Actually updating the position is "expensive" so we only do it once at the end
        }

        return new MoveHelperResult(finalPosition, finalVelocity);
    }

    /// <summary>
    /// Sometimes we have to update only the position but not the velocity (Like when climbing steps or getting unstuck) so we can't have Position rely only on Velocity
    /// </summary>
    public struct MoveHelperResult
    {
        public Vector3 Position;
        public Vector3 Velocity;

        public MoveHelperResult(Vector3 position, Vector3 velocity)
        {
            Position = position;
            Velocity = velocity;
        }
    }

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
            var groundTrace = BuildTrace(_shrunkenBounds, position, position + Vector3.Down * (GroundStickDistance + SkinWidth * 1.1f)); // Compensate for floating inaccuracy

            if (groundTrace.StartedSolid)
            {
                IsStuck = true;
                if (UnstuckEnabled)
                {
                    if (UnstuckTarget == null)
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
                var hasLanded = !IsOnGround && Velocity.z <= 0f && groundTrace.Hit && groundTrace.Distance <= SkinWidth * 2f; // Wasn't on the ground and now is
                var isGrounded = IsOnGround && groundTrace.Hit; // Was already on the ground and still is, this helps stick when going down stairs

                IsOnGround = hasLanded || isGrounded;
                GroundSurface = IsOnGround ? groundTrace.Surface : null;
                GroundNormal = IsOnGround ? groundTrace.Normal : Vector3.Up;
                GroundObject = IsOnGround ? groundTrace.GameObject : null;
                IsSlipping = IsOnGround && GroundAngle > MaxGroundAngle;

                if (IsSlipping && !gravityPass && velocity.z > 0f)
                    velocity = velocity.WithZ(0f); // If we're slipping ignore any extra velocity we had

                if (IsOnGround && GroundStickEnabled && !IsSlipping)
                {
                    position = groundTrace.EndPosition + Vector3.Up * SkinWidth; // Place on the ground
                    velocity = Vector3.VectorPlaneProject(velocity, GroundNormal); // Follow the ground you're on without projecting Z
                }

                IsStuck = false;
            }
        }

        if (velocity.IsNearlyZero(0.01f)) // Not worth continuing, reduces small stutter
        {
            return new MoveHelperResult(position, Vector3.Zero);
        }

        var toTravel = velocity.Length + SkinWidth;
        var targetPosition = position + velocity.Normal * toTravel;
        var travelTrace = BuildTrace(_shrunkenBounds, position, targetPosition);

        if (travelTrace.Hit)
        {
            var travelled = velocity.Normal * Math.Max(travelTrace.Distance - SkinWidth, 0f);

            var leftover = velocity - travelled; // How much leftover velocity still needs to be simulated
            var angle = Vector3.GetAngle(Vector3.Up, travelTrace.Normal);
            if (toTravel >= SkinWidth && travelTrace.Distance < SkinWidth)
                travelled = Vector3.Zero;

            if (angle <= MaxGroundAngle) // Terrain we can walk on
            {
                if (gravityPass || !IsOnGround)
                    leftover = Vector3.VectorPlaneProject(leftover, travelTrace.Normal); // Don't project the vertical velocity after landing else it boosts your horizontal velocity
                else
                    leftover = leftover.ProjectAndScale(travelTrace.Normal); // Project the velocity along the terrain
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
                            var stepHorizontal = velocity.WithZ(0f).Normal * StepDepth; // How far in front we're looking for steps
                            var stepVertical = Vector3.Up * (StepHeight + SkinWidth); // How high we're looking for steps + Some to compensate for floating inaccuracy
                            var stepTrace = BuildTrace(_shrunkenBounds, travelTrace.EndPosition + stepHorizontal + stepVertical, travelTrace.EndPosition + stepHorizontal);
                            var stepAngle = Vector3.GetAngle(stepTrace.Normal, Vector3.Up);

                            if (!stepTrace.StartedSolid && stepTrace.Hit && stepAngle <= MaxGroundAngle) // We found a step!
                            {
                                if (isStep || !IsSlipping && PseudoStepsEnabled)
                                {
                                    var stepDistance = stepTrace.EndPosition - travelTrace.EndPosition;
                                    var stepTravelled = Vector3.Up * stepDistance;
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
                    leftover = (wallLeftover * scale).WithZ(wallLeftover.z);

                    WallObject = travelTrace.GameObject;
                    WallNormal = travelTrace.Normal;
                }
                else
                {
                    if (!climbedStair)
                    {
                        var scale = IsSlipping ? 1f : 1f - Vector3.Dot(-travelTrace.Normal / GripFactorReduction, velocity.Normal);
                        leftover = ScaleAgainstWalls ? Vector3.VectorPlaneProject(leftover, travelTrace.Normal) * scale : leftover.ProjectAndScale(travelTrace.Normal);
                    }
                }
            }

            if (travelled.Length <= 0.01f && leftover.Length <= 0.01f)
                return new MoveHelperResult(position + travelled, travelled / delta);

            var newResult = CollideAndSlide(new MoveHelperResult(position + travelled, leftover / delta), delta, depth + 1, gravityPass); // Simulate another bounce for the leftover velocity from the latest position
            var currentResult = new MoveHelperResult(newResult.Position, travelled / delta + newResult.Velocity); // Use the new bounce's position and combine the velocities

            return currentResult;
        }

        if (depth == 0 && !gravityPass)
        {
            IsPushingAgainstWall = false;
            WallObject = null;
        }

        return new MoveHelperResult(position + velocity, velocity / delta); // We didn't hit anything? Ok just keep going then :-)
    }

    private float CalculateGoalSpeed(Vector3 wishVelocity, Vector3 velocity, bool isAccelerating, float delta)
    {
        float goalSpeed;

        var isSameDirection = velocity.IsNearlyZero(1f) || Vector3.Dot(wishVelocity.WithZ(0f).Normal, velocity.WithZ(0f).Normal) >= 0f; // Is our wishVelocity roughly moving towards our velocity already?

        var acceleration = IsOnGround ? GroundAcceleration : AirAcceleration;
        var deceleration = IsOnGround ? GroundDeceleration : AirDeceleration;

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

    public bool TryUnstuck(Vector3 position, out Vector3 result)
    {
        if (_lastVelocity == Vector3.Zero)
            _lastVelocity = Vector3.Down;

        var velocityLength = _lastVelocity.Length + SkinWidth;
        var startPos = position - _lastVelocity.Normal * velocityLength; // Try undoing the last velocity 1st
        var endPos = position;

        for (int i = 0; i < MaxUnstuckTries + 1; i++)
        {
            if (i == 1)
                startPos = position + Vector3.Up * 2f; // Try going up 2nd

            if (i > 1)
                startPos = position + Vector3.Random.Normal * ((float)i / 2f); // Start randomly checking 3rd

            if (startPos - endPos == Vector3.Zero) // No difference!
                continue;

            var unstuckTrace = BuildTrace(_shrunkenBounds, startPos, endPos);

            if (!unstuckTrace.StartedSolid)
            {
                result = unstuckTrace.EndPosition - _lastVelocity.Normal * SkinWidth;
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

        if (!ManuallyUpdate && Active)
            Move();
    }
}
