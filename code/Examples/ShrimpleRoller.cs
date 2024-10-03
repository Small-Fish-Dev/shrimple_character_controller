namespace ShrimpleCharacterController;

[Hide]
public sealed class ShrimpleRoller : Component
{
    [RequireComponent]
    public ShrimpleCharacterController Controller { get; set; }

    public ModelRenderer Renderer { get; set; }
    public GameObject Camera { get; set; }

    [Property]
    [Range(500f, 2000f, 100f)]
    public float WalkSpeed { get; set; } = 1000f;

    [Property]
    [Range(1000f, 5000f, 200f)]
    public float RunSpeed { get; set; } = 3000f;

    [Property]
    [Range(200f, 500f, 20f)]
    public float JumpStrength { get; set; } = 350f;

    public Angles EyeAngles { get; set; }

    protected override void OnStart()
    {
        base.OnStart();

        Renderer = Components.Get<ModelRenderer>(FindMode.EnabledInSelfAndDescendants);

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
        var wishSpeed = isRunning ? RunSpeed : WalkSpeed;

        Controller.WishVelocity = wishDirection * wishSpeed;
        Controller.Move();

        if (Input.Pressed("Jump") && Controller.IsOnGround)
            Controller.Punch(Vector3.Up * JumpStrength);
    }

    protected override void OnUpdate()
    {
        base.OnUpdate();

        EyeAngles += Input.AnalogLook;
        EyeAngles = EyeAngles.WithPitch(MathX.Clamp(EyeAngles.pitch, -10f, 40f));

        float pitchRotation = Controller.Velocity.x * Time.Delta;
        float rollRotation = -Controller.Velocity.y * Time.Delta;
        var ballPitch = Rotation.FromPitch(pitchRotation);
        var ballRoll = Rotation.FromRoll(rollRotation);
        Renderer.WorldRotation = ballPitch * ballRoll * Renderer.WorldRotation;

        var cameraOffset = Vector3.Up * 70f + Vector3.Backward * 260f;
        Camera.WorldRotation = EyeAngles.ToRotation();
        Camera.LocalPosition = cameraOffset * Camera.WorldRotation;
    }
}
