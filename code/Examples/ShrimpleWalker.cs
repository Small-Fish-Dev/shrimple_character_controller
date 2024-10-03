using Sandbox.Citizen;

namespace ShrimpleCharacterController;

[Hide]
public sealed class ShrimpleWalker : Component
{
    [RequireComponent]
    public ShrimpleCharacterController Controller { get; set; }

    [RequireComponent]
    public CitizenAnimationHelper AnimationHelper { get; set; }
    public SkinnedModelRenderer Renderer { get; set; }
    public GameObject Camera { get; set; }

    [Property]
    [Range(50f, 200f, 10f)]
    public float WalkSpeed { get; set; } = 100f;

    [Property]
    [Range(100f, 500f, 20f)]
    public float RunSpeed { get; set; } = 300f;

    [Property]
    [Range(25f, 100f, 5f)]
    public float DuckSpeed { get; set; } = 50f;

    [Property]
    [Range(200f, 500f, 20f)]
    public float JumpStrength { get; set; } = 350f;

    public Angles EyeAngles { get; set; }

    protected override void OnStart()
    {
        base.OnStart();

        Renderer = Components.Get<SkinnedModelRenderer>(FindMode.EverythingInSelfAndDescendants);
        Camera = new GameObject(true, "Camera");
        Camera.SetParent(GameObject);
        var cameraComponent = Camera.Components.Create<CameraComponent>();
        cameraComponent.ZFar = 32768f;
    }

    protected override void OnFixedUpdate()
    {
        base.OnFixedUpdate();

        var wishDirection = Input.AnalogMove.Normal * Rotation.FromYaw(EyeAngles.yaw);
        var isDucking = Input.Down("Duck");
        var isRunning = Input.Down("Run");
        var wishSpeed = isDucking ? DuckSpeed :
            isRunning ? RunSpeed : WalkSpeed;

        Controller.WishVelocity = wishDirection * wishSpeed;
        Controller.Move();

        if (Input.Pressed("Jump") && Controller.IsOnGround)
        {
            Controller.Punch(Vector3.Up * JumpStrength);
            AnimationHelper?.TriggerJump();
        }

        if (!AnimationHelper.IsValid()) return;

        AnimationHelper.WithWishVelocity(Controller.WishVelocity);
        AnimationHelper.WithVelocity(Controller.Velocity);
        AnimationHelper.DuckLevel = isDucking ? 1f : 0f;
        AnimationHelper.IsGrounded = Controller.IsOnGround;
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        EyeAngles += Input.AnalogLook;
        EyeAngles = EyeAngles.WithPitch(MathX.Clamp(EyeAngles.pitch, -10f, 40f));
        Renderer.WorldRotation = Rotation.Slerp(Renderer.WorldRotation, Rotation.FromYaw(EyeAngles.yaw), Time.Delta * 5f);

        var cameraOffset = Vector3.Up * 70f + Vector3.Backward * 220f;
        Camera.WorldRotation = EyeAngles.ToRotation();
        Camera.LocalPosition = cameraOffset * Camera.WorldRotation;
    }
}
