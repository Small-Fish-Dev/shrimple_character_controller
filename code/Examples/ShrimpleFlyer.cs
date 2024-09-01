using Sandbox.Citizen;

namespace ShrimpleCharacterController;

public sealed class ShrimpleFlyer : Component
{
	[RequireComponent]
	public ShrimpleCharacterController Controller { get; set; }

	[RequireComponent]
	public CitizenAnimationHelper AnimationHelper { get; set; }
	public SkinnedModelRenderer Renderer { get; set; }
	public GameObject Camera { get; set; }

	[Property]
	[Range( 400f, 1600f, 20f )]
	public float WalkSpeed { get; set; } = 800f;

	[Property]
	[Range( 800f, 4000f, 80f )]
	public float RunSpeed { get; set; } = 2400f;

	public Angles EyeAngles { get; set; }

	protected override void OnStart()
	{
		base.OnStart();

		Renderer = AnimationHelper.Target;
		Camera = new GameObject( true, "Camera" );
		Camera.SetParent( GameObject );
		var cameraComponent = Camera.Components.Create<CameraComponent>();
		cameraComponent.ZFar = 32768f;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();

		var isDucking = Input.Down( "Duck" );
		var isRunning = Input.Down( "Run" );
		var ascending = Input.Down( "Jump" ) ? 1f : 0f;
		var descending = Input.Down( "Duck" ) ? -1f : 0f;
		var wishSpeed = isRunning ? RunSpeed : WalkSpeed;
		var wishDirection = (Input.AnalogMove + Vector3.Up * (ascending + descending)).Normal * EyeAngles.ToRotation();

		Controller.WishVelocity = wishDirection * wishSpeed;
		Controller.Move();

		AnimationHelper.WithWishVelocity( Controller.WishVelocity );
		AnimationHelper.WithVelocity( Controller.Velocity );
		AnimationHelper.IsGrounded = Controller.IsOnGround;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		EyeAngles += Input.AnalogLook;
		EyeAngles = EyeAngles.WithPitch( MathX.Clamp( EyeAngles.pitch, -10f, 40f ) );
		Renderer.Transform.Rotation = Rotation.Slerp( Renderer.Transform.Rotation, Rotation.FromYaw( EyeAngles.yaw ), Time.Delta * 5f );

		var cameraOffset = Vector3.Up * 70f + Vector3.Backward * 220f;
		Camera.Transform.Rotation = EyeAngles.ToRotation();
		Camera.Transform.LocalPosition = cameraOffset * Camera.Transform.Rotation;
	}
}