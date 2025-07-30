using Sandbox;

public sealed class PlatformTest : Component
{
    protected override void OnUpdate()
    {
        WorldPosition += Vector3.Right * (float)Math.Sin(Time.Now / 2f) * 100f * Time.Delta;
    }
}
